using System.Net.Sockets;
using Sproto;

namespace Im.Connection;

/// <summary>
/// TCP 连接状态机。纯传输层：管理 socket 生命周期与状态转换。
/// </summary>
/// <remarks>
/// 职责边界：本类只管 TCP socket 的建立/断开、通用 sproto 请求收发、
/// 状态转换及事件通知。不认识任何具体协议（login/register/keepAlive），
/// 不打印日志——由上层通过事件回调自行决定输出。
///
/// 状态转换：
/// <code>
/// Disconnected ──ConnectAsync──▶ Connecting ──socket建立──▶ Connected
///                                                            │
///                                                     SetAuthenticated
///                                                            ▼
///              Disconnected ◀──socket断/Disconnect── Authenticated
/// </code>
///
/// 持有：socket+stream（网络资源）、token+sessionId+name（Authenticated 状态附属数据）。
/// 所有资源在 Disconnected 时统一清理。
/// </remarks>
public sealed class TcpSessionManager : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly SprotoRpc _rpc;
    private readonly Func<long> _nextSession;
    private readonly object _lock = new();

    private TcpClient? _tcp;
    private NetworkStream? _stream;

    // Authenticated 状态附属数据——由业务层通过 SetAuthenticated 写入
    private string? _token;
    private string? _displayName;
    private long? _sessionId;

    private TcpState _state = TcpState.Disconnected;
    private bool _disposed;

    /// <summary>
    /// TCP 连接状态枚举。
    /// </summary>
    public enum TcpState
    {
        /// <summary>未连接。</summary>
        Disconnected,
        /// <summary>正在建立 TCP 连接。</summary>
        Connecting,
        /// <summary>TCP 已连接，未登录。</summary>
        Connected,
        /// <summary>已登录（持有有效 token）。</summary>
        Authenticated
    }

    /// <summary>
    /// 当前连接状态。
    /// </summary>
    public TcpState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>
    /// 是否已建立 TCP 连接（Connected 或 Authenticated）。
    /// </summary>
    public bool IsConnected
    {
        get { lock (_lock) return _state is TcpState.Connected or TcpState.Authenticated; }
    }

    /// <summary>
    /// 是否已登录（持有有效 token）。
    /// </summary>
    public bool IsLoggedIn
    {
        get { lock (_lock) return !string.IsNullOrEmpty(_token); }
    }

    /// <summary>
    /// 显示名称，未登录时为 null。
    /// </summary>
    public string? DisplayName
    {
        get { lock (_lock) return _displayName; }
    }

    /// <summary>
    /// 登录令牌，未登录时为 null。
    /// </summary>
    public string? Token
    {
        get { lock (_lock) return _token; }
    }

    /// <summary>
    /// 服务端分配的会话 ID，未登录时为 null。
    /// </summary>
    public long? SessionId
    {
        get { lock (_lock) return _sessionId; }
    }

    /// <summary>
    /// 状态转换时触发。参数为 (旧状态, 新状态)。
    /// </summary>
    public event Action<TcpState, TcpState>? StateChanged;

    /// <summary>
    /// TCP 连接意外断开（发送/接收失败）时触发。
    /// 触发时状态已转为 Disconnected。异步触发以避免回调死锁。
    /// 主动 <see cref="Disconnect"/> 不触发此事件。
    /// </summary>
    public event Action? ConnectionLost;

    /// <summary>
    /// 初始化 TCP 会话管理器。
    /// </summary>
    /// <param name="rpc">sproto RPC 实例，用于请求打包和响应解包。</param>
    /// <param name="nextSession">RPC 会话 ID 生成器，每次调用递增。</param>
    public TcpSessionManager(SprotoRpc rpc, Func<long> nextSession)
    {
        _rpc = rpc;
        _nextSession = nextSession;
    }

    private (TcpState old, TcpState newVal)? _pendingStateChange;

    /// <summary>
    /// 锁内设置状态，事件延迟到锁外触发。
    /// </summary>
    private void SetStateLocked(TcpState newState)
    {
        TcpState old = _state;
        if (old == newState) return;
        _state = newState;
        _pendingStateChange = (old, newState);
    }

    /// <summary>
    /// 触发挂起的状态变更事件（锁外调用）。
    /// </summary>
    private void FirePendingStateChanged()
    {
        var pending = _pendingStateChange;
        if (pending == null) return;
        _pendingStateChange = null;
        StateChanged?.Invoke(pending.Value.old, pending.Value.newVal);
    }

    /// <summary>
    /// 建立 TCP 连接。Disconnected → Connecting → Connected。
    /// </summary>
    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_state != TcpState.Disconnected)
                throw new InvalidOperationException($"当前状态 {_state} 不允许连接");
            SetStateLocked(TcpState.Connecting);
        }
        FirePendingStateChanged();

        try
        {
            _tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ConnectTimeout);
            await _tcp.ConnectAsync(host, port, cts.Token);
            _stream = _tcp.GetStream();

            lock (_lock) SetStateLocked(TcpState.Connected);
            FirePendingStateChanged();
        }
        catch
        {
            TransitionToDisconnected();
            // 异步触发，避免调用方在 await 后的 catch 里被事件回调阻塞
            _ = Task.Run(() => ConnectionLost?.Invoke());
            throw;
        }
    }

    /// <summary>
    /// 发送 sproto 请求并读取响应。
    /// 协议格式：2 字节大端长度前缀 + sproto_pack 数据。
    /// 业务层用此方法发送任何协议（login/register/keepAlive 等）。
    /// 收发失败时自动转为 Disconnected 并触发 <see cref="ConnectionLost"/>。
    /// </summary>
    /// <param name="proto">协议名，如 "login"、"register"、"keepAlive"。</param>
    /// <param name="request">sproto request 对象。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<RpcMessage> SendRequestAsync(string proto, SprotoObject request, CancellationToken ct)
    {
        NetworkStream? stream;
        lock (_lock)
        {
            if (_state is not (TcpState.Connected or TcpState.Authenticated))
                throw new InvalidOperationException($"当前状态 {_state} 不允许发送");
            stream = _stream;
        }
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            RpcPackage pkg = _rpc.PackRequest(proto, request, _nextSession());

            byte[] packet = new byte[2 + pkg.size];
            packet[0] = (byte)((pkg.size >> 8) & 0xFF);
            packet[1] = (byte)(pkg.size & 0xFF);
            Array.Copy(pkg.data, 0, packet, 2, pkg.size);
            await stream.WriteAsync(packet, ct);

            byte[] lenBuf = new byte[2];
            await stream.ReadExactlyAsync(lenBuf, 0, 2, ct);
            int respLen = (lenBuf[0] << 8) | lenBuf[1];
            byte[] packedResp = new byte[respLen];
            await stream.ReadExactlyAsync(packedResp, 0, respLen, ct);

            return _rpc.UnpackMessage(packedResp, respLen);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 主动取消，不当作连接丢失
            throw;
        }
        catch
        {
            // socket 异常 → 断开 + 异步通知（避免回调里调 Disconnect 死锁）
            TransitionToDisconnected();
            _ = Task.Run(() => ConnectionLost?.Invoke());
            throw;
        }
    }

    /// <summary>
    /// 标记已认证。业务层登录成功后调用：保存 token/sessionId/name 并转 Authenticated。
    /// </summary>
    public void SetAuthenticated(string token, long? sessionId, string? name)
    {
        lock (_lock)
        {
            if (_state != TcpState.Connected)
                throw new InvalidOperationException($"当前状态 {_state} 不允许标记认证");
            if (string.IsNullOrEmpty(token))
                throw new ArgumentException("token 不能为空", nameof(token));

            _token = token;
            _sessionId = sessionId;
            _displayName = string.IsNullOrEmpty(name) ? null : name;
            SetStateLocked(TcpState.Authenticated);
        }
        FirePendingStateChanged();
    }

    /// <summary>
    /// 主动断开。任意状态 → Disconnected。不触发 <see cref="ConnectionLost"/>。
    /// </summary>
    public void Disconnect()
    {
        TransitionToDisconnected();
    }

    /// <summary>
    /// 内部断开：清登录态 + 关 socket + 转 Disconnected + 触发 StateChanged。
    /// </summary>
    private void TransitionToDisconnected()
    {
        lock (_lock)
        {
            if (_state == TcpState.Disconnected) return;

            _token = null;
            _displayName = null;
            _sessionId = null;

            try { _tcp?.Dispose(); } catch { }
            _tcp = null;
            _stream = null;

            SetStateLocked(TcpState.Disconnected);
        }
        FirePendingStateChanged();
    }

    /// <summary>
    /// 释放所有资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TransitionToDisconnected();
    }
}
