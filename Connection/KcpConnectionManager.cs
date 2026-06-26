using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Im.Config;
using System.Net.Sockets.Kcp;

namespace Im.Connection;

/// <summary>
/// KCP 连接管理器。负责 KCP over UDP 的连接生命周期：
/// 建立 KCP 会话、收发应用层消息、驱动 KCP 状态机、断开清理。
/// 基于 KumoKyaku/KCP 库（标准裸 KCP 段，与服务端 lkcp 兼容）。
/// </summary>
public sealed class KcpConnectionManager : IDisposable
{
    private readonly object _stateLock = new();
    private Socket? _socket;
    private EndPoint? _remoteEndPoint;
    private SimpleSegManager.Kcp? _kcp;
    private CancellationTokenSource? _cts;
    private Task? _udpReceiveTask;
    private Task? _updateTask;
    private bool _connected;
    private bool _disposed;

    // 原始字节请求-响应：用于 sproto 协议在 KCP 上的收发
    private readonly object _rawLock = new();
    private TaskCompletionSource<byte[]>? _rawResponseTcs;
    private readonly System.Diagnostics.Stopwatch _deadLinkTimer = System.Diagnostics.Stopwatch.StartNew();
    private int _lastWaitSnd;
    private const long DeadTimeoutMs = 10_000;

    /// <summary>
    /// 获取当前是否已建立连接。
    /// </summary>
    public bool IsConnected
    {
        get { lock (_stateLock) return _connected; }
    }

    /// <summary>
    /// 收到远程主机消息时触发。
    /// </summary>
    public event Action<string>? MessageReceived;

    /// <summary>
    /// 连接意外断开时触发。
    /// </summary>
    public event Action? ConnectionLost;

    /// <summary>
    /// 建立到指定主机的 KCP 连接。
    /// 使用时间戳随机生成 conv，连接后立即发送 "CONNECT" 握手。
    /// </summary>
    public async Task<bool> ConnectAsync(AppConfig config, CancellationToken ct)
    {
        lock (_stateLock)
        {
            if (_connected)
            {
                Console.WriteLine("[WARN] 已连接，请先执行 'disconnect'。");
                return false;
            }
        }

        Console.WriteLine($"[INFO] 正在连接 {config.Host}:{config.Port}（KCP 协议）...");

        try
        {
            var endPoint = new IPEndPoint(IPAddress.Parse(config.Host), config.Port);
            _socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            _remoteEndPoint = endPoint;

            // 随机生成 conv（取时间戳低 32 位）
            uint conv = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // KCP 实例 —— SimpleSegManager.Kcp 自动设置 SegmentManager，避免空引用
            _kcp = new SimpleSegManager.Kcp(conv, new KcpCallback((buffer, length) =>
            {
                try
                {
                    _socket.SendTo(buffer.Memory.Span.Slice(0, length), SocketFlags.None, _remoteEndPoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] KCP Output 发送失败：{ex.Message}");
                }
                finally
                {
                    buffer.Dispose();
                }
            }));

            // 配置 —— 与服务端 lkcp_nodelay(1,10,2,1) + wndsize(128,128) 一致
            _kcp.NoDelay(1, 10, 2, 1);
            _kcp.WndSize(128, 128);
            _kcp.SetMtu(1400);

            // 启动接收 + 更新循环
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _udpReceiveTask = UdpReceiveLoopAsync(_cts.Token);
            _updateTask = UpdateLoopAsync(_cts.Token);

            // 发送 "CONNECT" 握手消息（ASCII 编码）
            byte[] handshake = Encoding.ASCII.GetBytes("CONNECT");
            _kcp.Send(handshake.AsSpan());
            _kcp.Update(DateTimeOffset.UtcNow);

            lock (_stateLock)
            {
                _connected = true;
            }

            Console.WriteLine($"[OK] 已连接到 {config.Host}:{config.Port}（会话 ID：{conv}，0x{conv:x8}）");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 连接失败：{ex.Message}");
            CleanupConnection();
            return false;
        }
    }

    /// <summary>
    /// 优雅断开远程连接并清理资源。
    /// </summary>
    public void Disconnect()
    {
        lock (_stateLock)
        {
            if (!_connected)
            {
                Console.WriteLine("[INFO] 当前未连接。");
                return;
            }
        }

        Console.WriteLine("[INFO] 正在断开...");
        CleanupConnection();
        Console.WriteLine("[OK] 已断开。");
    }

    /// <summary>
    /// 通过 KCP 连接发送原始字节数据。
    /// </summary>
    public Task<bool> SendRawAsync(byte[] data, CancellationToken ct)
    {
        SimpleSegManager.Kcp? kcp;
        lock (_stateLock)
        {
            if (!_connected || _kcp is null)
            {
                Console.WriteLine("[ERROR] 未连接，请先执行 'connect'。");
                return Task.FromResult(false);
            }
            kcp = _kcp;
        }

        try
        {
            kcp.Send(data.AsSpan());
            kcp.Update(DateTimeOffset.UtcNow);

            _deadLinkTimer.Restart();
            _lastWaitSnd = kcp.WaitSnd;

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 发送失败：{ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 等待下一个通过 KCP 到达的原始字节响应（仅一次）。
    /// 与 <see cref="SendRawAsync"/> 配合实现请求-响应模式。
    /// </summary>
    public Task<byte[]> WaitForRawResponseAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<byte[]>();
        lock (_rawLock)
        {
            _rawResponseTcs = tcs;
        }

        if (ct.CanBeCanceled)
        {
            ct.Register(() => tcs.TrySetCanceled());
        }

        return tcs.Task;
    }

    /// <summary>
    /// 通过 KCP 连接发送文本消息。
    /// 数据入队后立即触发一次 Update 以刷出（不等下一个 10ms tick）。
    /// </summary>
    public Task<bool> SendMessageAsync(string message, CancellationToken ct)
    {
        SimpleSegManager.Kcp? kcp;
        lock (_stateLock)
        {
            if (!_connected || _kcp is null)
            {
                Console.WriteLine("[ERROR] 未连接，请先执行 'connect'。");
                return Task.FromResult(false);
            }
            kcp = _kcp;
        }

        if (string.IsNullOrEmpty(message))
        {
            Console.WriteLine("[WARN] 消息为空，无需发送。");
            return Task.FromResult(false);
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);

            // Send 只入队，数据在下一个 Update/Flush 时发出
            kcp.Send(data.AsSpan());

            // 立即触发一次 Update，将数据刷出
            kcp.Update(DateTimeOffset.UtcNow);

            // 重置进度时钟，开始监测对端是否可达
            _deadLinkTimer.Restart();
            _lastWaitSnd = kcp.WaitSnd;

            Console.WriteLine($"[SENT] {message}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 发送失败：{ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// UDP 接收循环：从 socket 读取裸包 → 喂入 Kcp.Input。
    /// </summary>
    private async Task UdpReceiveLoopAsync(CancellationToken ct)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(4096, pinned: true);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                SocketReceiveFromResult result;
                try
                {
                    result = await _socket!.ReceiveFromAsync(buffer, SocketFlags.None, _remoteEndPoint!, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[ERROR] UDP 接收错误：{ex.Message}");
                    break;
                }

                // 喂入 KCP 协议栈
                int ret = _kcp!.Input(buffer.AsSpan(0, result.ReceivedBytes));
                if (ret < 0)
                {
                    Console.WriteLine($"\n[WARN] KCP Input 返回异常：{ret}");
                }
                else
                {
                    // 收到任何有效 UDP 包，说明对端可达，重置进度时钟
                    _deadLinkTimer.Restart();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 关闭时预期会触发取消
        }
    }

    /// <summary>
    /// KCP 更新循环：每 10ms 驱动 KCP 状态机（发送 ACK、重传等），并收取完整应用层消息。
    /// 同时检测发送队列是否长时间无进展（对端不可达），自动断开。
    /// </summary>
    private async Task UpdateLoopAsync(CancellationToken ct)
    {
        byte[] appBuf = new byte[65536];
        bool deadLinkNotified = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(10, ct);

                // 驱动 KCP 状态机（flush 数据、重传、窗口探测等）
                _kcp!.Update(DateTimeOffset.UtcNow);

                // 收取所有可用的完整消息（收到消息说明链路正常，重置进度时钟）
                bool receivedAny = false;
                while (true)
                {
                    int n = _kcp.Recv(appBuf.AsSpan());
                    if (n <= 0)
                        break;

                    receivedAny = true;

                    // 优先投递给原始字节等待者（sproto 请求-响应）
                    TaskCompletionSource<byte[]>? rawTcs;
                    lock (_rawLock)
                    {
                        rawTcs = _rawResponseTcs;
                        if (rawTcs != null)
                            _rawResponseTcs = null;
                    }

                    if (rawTcs != null)
                    {
                        var response = new byte[n];
                        Array.Copy(appBuf, response, n);
                        rawTcs.TrySetResult(response);
                    }
                    else
                    {
                        string msg = Encoding.UTF8.GetString(appBuf, 0, n);
                        MessageReceived?.Invoke(msg);
                    }
                }
                if (receivedAny)
                    _deadLinkTimer.Restart();

                // 检测发送队列进展
                int curWaitSnd = _kcp.WaitSnd;
                if (curWaitSnd < _lastWaitSnd)
                {
                    // 等待队列减少 → 有 ACK 回来，链路正常
                    _deadLinkTimer.Restart();
                }
                _lastWaitSnd = curWaitSnd;

                // 发送队列卡住超过超时 → 判定对端不可达
                if (curWaitSnd > 0 && _deadLinkTimer.ElapsedMilliseconds > DeadTimeoutMs && !deadLinkNotified)
                {
                    deadLinkNotified = true;
                    Console.WriteLine("\n[ERROR] 服务器无响应，连接断开。");
                    // 由 CleanupConnection 触发 ConnectionLost
                    _ = Task.Run(() => Disconnect(), CancellationToken.None);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 关闭时预期会触发取消
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] KCP 更新循环异常：{ex.Message}");
        }
        finally
        {
            lock (_stateLock)
            {
                if (_connected)
                {
                    _connected = false;
                    ConnectionLost?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// 清理所有连接资源。
    /// </summary>
    private void CleanupConnection()
    {
        lock (_stateLock)
        {
            _connected = false;
        }

        _cts?.Cancel();

        // 等待后台循环退出（仅做清理，忽略异常）
        try { _udpReceiveTask?.GetAwaiter().GetResult(); } catch { /* 清理阶段忽略异常 */ }
        try { _updateTask?.GetAwaiter().GetResult(); } catch { /* 清理阶段忽略异常 */ }
        try { _updateTask?.Dispose(); } catch { /* 清理阶段忽略异常 */ }

        _kcp?.Dispose();
        _kcp = null;

        try { _socket?.Dispose(); } catch { /* 清理阶段忽略异常 */ }
        _socket = null;

        _cts?.Dispose();
        _cts = null;
        _udpReceiveTask = null;
        _updateTask = null;
    }

    /// <summary>
    /// 释放连接管理器使用的所有资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupConnection();
    }
}

/// <summary>
/// 实现 <see cref="IKcpCallback"/>，将 KCP 产出的数据段通过 UDP 发送。
/// </summary>
file sealed class KcpCallback : IKcpCallback
{
    private readonly Action<IMemoryOwner<byte>, int> _onOutput;

    public KcpCallback(Action<IMemoryOwner<byte>, int> onOutput)
    {
        _onOutput = onOutput;
    }

    public void Output(IMemoryOwner<byte> buffer, int avalidLength)
    {
        _onOutput(buffer, avalidLength);
    }
}
