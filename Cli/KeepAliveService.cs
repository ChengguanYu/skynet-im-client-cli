using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// keepAlive 定时服务（业务层）。
/// 每 60 秒通过 <see cref="TcpSessionManager"/> 发送 keepAlive.REQUEST，维持登录态。
/// </summary>
/// <remarks>
/// 职责边界：本服务只管 keepAlive 协议的构造和定时调度。
/// token/sessionId 从 <see cref="TcpSessionManager"/> 读取（状态机管理），
/// 发送失败由 <see cref="TcpSessionManager"/> 自动转为 Disconnected 并触发 ConnectionLost，
/// 本服务检测到异常后停止循环。
/// </remarks>
public sealed class KeepAliveService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly SprotoRpc _rpc;
    private readonly TcpSessionManager _tcp;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>
    /// 初始化 keepAlive 服务。
    /// </summary>
    /// <param name="rpc">sproto RPC 实例，用于构造 keepAlive 请求。</param>
    /// <param name="tcp">TCP 会话状态机，提供通用收发和登录态读取。</param>
    public KeepAliveService(SprotoRpc rpc, TcpSessionManager tcp)
    {
        _rpc = rpc;
        _tcp = tcp;
    }

    /// <summary>
    /// 启动定时 keepAlive。若已有任务在运行，先停止旧任务。
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            StopLocked();
            _cts = new CancellationTokenSource();
            _loopTask = LoopAsync(_cts.Token);
        }
    }

    /// <summary>
    /// 停止定时任务。
    /// </summary>
    public void Stop()
    {
        lock (_lock) StopLocked();
    }

    /// <summary>
    /// 当前是否正在运行。
    /// </summary>
    public bool IsRunning
    {
        get { lock (_lock) return _loopTask is not null && !_loopTask.IsCompleted; }
    }

    /// <summary>
    /// 定时循环：等待 tick → 发送 keepAlive → 等待下一个 tick。
    /// 基于 <see cref="PeriodicTimer"/>，空闲时零线程开销。
    /// </summary>
    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await SendKeepAliveAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    /// <summary>
    /// 构造并发送一次 keepAlive.REQUEST。
    /// token/sessionId 从状态机读取，本服务不持有副本。
    /// </summary>
    private async Task SendKeepAliveAsync(CancellationToken ct)
    {
        string? token = _tcp.Token;
        long? sessionId = _tcp.SessionId;
        if (string.IsNullOrEmpty(token))
            return;

        try
        {
            // proto: keepAlive.request { session 0 : string; token 1 : string }
            var req = _rpc.C2S.NewSprotoObject("keepAlive.request");
            req["session"] = sessionId?.ToString() ?? "";
            req["token"] = token;

            await _tcp.SendRequestAsync("keepAlive", req, ct).ConfigureAwait(false);
            Console.WriteLine("[KEEPALIVE] 已发送 keepAlive.REQUEST");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 发送失败 — TcpSessionManager 已转 Disconnected + 触发 ConnectionLost
            // 停止循环，避免后续 tick 对已断开的连接发送
            // 只 Cancel 不 Wait（当前在循环内，Wait 会死锁）
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// 锁内停止：cancel + wait + dispose。
    /// </summary>
    private void StopLocked()
    {
        _cts?.Cancel();

        if (_loopTask is not null)
        {
            try { _loopTask.Wait(TimeSpan.FromSeconds(3)); }
            catch { /* 超时或聚合异常，dispose 兜底 */ }
            try { _loopTask.Dispose(); } catch { }
        }
        _loopTask = null;

        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        Stop();
    }
}
