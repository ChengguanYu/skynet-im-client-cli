using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// KCP keepAlive 定时服务。
/// 每 60 秒通过 <see cref="KcpRpcDispatcher"/> 发送 keepAlive.REQUEST，维持 KCP 会话。
/// 复用现有 keepAlive 协议（tag 3），token/sessionId 从 <see cref="TcpSessionManager"/> 读取。
/// </summary>
/// <remarks>
/// 结构与 <see cref="KeepAliveService"/> 一致，发送通道改为 KcpRpcDispatcher。
/// 发送失败时自动停止循环；KCP 断连时由 CommandHandler 的 OnKcpConnectionLost 兜底停止。
/// </remarks>
public sealed class KcpKeepAliveService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly SprotoRpc _rpc;
    private readonly TcpSessionManager _tcp;
    private readonly KcpRpcDispatcher _kcpDispatcher;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public KcpKeepAliveService(SprotoRpc rpc, TcpSessionManager tcp, KcpRpcDispatcher kcpDispatcher)
    {
        _rpc = rpc;
        _tcp = tcp;
        _kcpDispatcher = kcpDispatcher;
    }

    public void Start()
    {
        lock (_lock)
        {
            StopLocked();
            _cts = new CancellationTokenSource();
            _loopTask = LoopAsync(_cts.Token);
        }
    }

    public void Stop()
    {
        lock (_lock) StopLocked();
    }

    public bool IsRunning
    {
        get { lock (_lock) return _loopTask is not null && !_loopTask.IsCompleted; }
    }

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

            await _kcpDispatcher.SendRequestAsync("keepAlive", req, ct).ConfigureAwait(false);
            Console.WriteLine("[KCP-KEEPALIVE] 已发送 keepAlive.REQUEST");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 发送失败 — 停止循环，KcpConnectionLost 兜底
            _cts?.Cancel();
        }
    }

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

    public void Dispose()
    {
        Stop();
    }
}
