using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// KCP 请求-响应分发器。按 session ID 匹配请求与响应，
/// 推送消息走独立事件，避免"先到先得"的竞态。
/// </summary>
public sealed class KcpRpcDispatcher : IDisposable
{
    private readonly SprotoRpc _rpc;
    private readonly KcpConnectionManager _kcp;
    private long _nextSessionId;

    // sessionId → 等待方 TCS
    private readonly Dictionary<long, TaskCompletionSource<RpcMessage>> _pending = new();

    private long NextSessionId() => Interlocked.Increment(ref _nextSessionId);

    /// <summary>
    /// 推送消息事件：无需 session 匹配的消息（notify/room_message 等）。
    /// </summary>
    public event Action<RpcMessage>? PushReceived;

    public KcpRpcDispatcher(SprotoRpc rpc, KcpConnectionManager kcp)
    {
        _rpc = rpc;
        _kcp = kcp;
        _kcp.MessageReceived += OnRawData;
        _kcp.ConnectionLost += OnConnectionLost;
    }

    /// <summary>
    /// 发送 sproto 请求并等待匹配的响应。
    /// session ID 自动生成，保证唯一。
    /// </summary>
    public async Task<RpcMessage> SendRequestAsync(string proto, SprotoObject request, CancellationToken ct)
    {
        long sessionId = NextSessionId();
        RpcPackage pkg = _rpc.PackRequest(proto, request, sessionId);

        byte[] sendBuf = new byte[pkg.size];
        Array.Copy(pkg.data, sendBuf, pkg.size);

        // 先注册 TCS，再发送——避免响应在注册前到达
        var tcs = new TaskCompletionSource<RpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending)
        {
            _pending[sessionId] = tcs;
        }

        // 取消时清理 _pending 条目
        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                lock (_pending)
                {
                    if (_pending.TryGetValue(sessionId, out var pendingTcs) && pendingTcs == tcs)
                        _pending.Remove(sessionId);
                }
                tcs.TrySetCanceled(ct);
            });
        }

        bool sent = await _kcp.SendRawAsync(sendBuf, ct);
        if (!sent)
        {
            lock (_pending)
            {
                if (_pending.TryGetValue(sessionId, out var pendingTcs) && pendingTcs == tcs)
                    _pending.Remove(sessionId);
            }
            tcs.TrySetException(new InvalidOperationException("KCP 发送请求失败"));
        }

        return await tcs.Task;
    }

    /// <summary>
    /// KCP 裸数据回调：解码 → 区分 response/push → 分发。
    /// </summary>
    private void OnRawData(byte[] data, int length)
    {
        RpcMessage msg;
        try
        {
            msg = _rpc.UnpackMessage(data, length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] KcpRpcDispatcher: 解析 sproto 失败：{ex.Message}");
            return;
        }

        if (msg.type == "response")
        {
            TaskCompletionSource<RpcMessage>? tcs;
            lock (_pending)
            {
                if (_pending.TryGetValue(msg.session, out tcs))
                    _pending.Remove(msg.session);
            }

            if (tcs != null)
            {
                tcs.TrySetResult(msg);
            }
            else
            {
                Console.WriteLine($"[WARN] KcpRpcDispatcher: 收到未知 session {msg.session} 的响应");
            }
        }
        else if (msg.type == "request")
        {
            // 推送消息（notify、room_message 等），session=0 或无等待方
            PushReceived?.Invoke(msg);
        }
    }

    /// <summary>
    /// KCP 连接断开时，取消所有等待中的请求。
    /// </summary>
    private void OnConnectionLost()
    {
        List<TaskCompletionSource<RpcMessage>> pendingList;
        lock (_pending)
        {
            pendingList = new List<TaskCompletionSource<RpcMessage>>(_pending.Values);
            _pending.Clear();
        }
        foreach (var tcs in pendingList)
        {
            tcs.TrySetCanceled();
        }
    }

    /// <summary>
    /// 释放资源，取消所有待处理请求。
    /// </summary>
    public void Dispose()
    {
        _kcp.MessageReceived -= OnRawData;
        _kcp.ConnectionLost -= OnConnectionLost;
        OnConnectionLost();
    }
}
