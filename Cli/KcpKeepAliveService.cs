using Im.Connection;
using Sproto;

namespace Im.Cli;

public sealed class KcpKeepAliveService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

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
            _cts?.Cancel();
        }
    }

    private void StopLocked()
    {
        _cts?.Cancel();

        if (_loopTask is not null)
        {
            try { _loopTask.Wait(TimeSpan.FromSeconds(3)); }
            catch { }
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
