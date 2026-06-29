using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// KCP 通知服务。处理服务端推送的 <c>notify.request</c>，
/// 打印通知并回复 <c>copy=true</c>。
/// 输入来自 <see cref="KcpRpcDispatcher.PushReceived"/> 事件。
/// </summary>
public sealed class NotifyService : IDisposable
{
    private readonly SprotoRpc _rpc;
    private readonly KcpConnectionManager _kcp;
    private bool _disposed;

    /// <summary>
    /// 通知打印后回调，用于重绘提示符。
    /// </summary>
    public Action? OnNotifyPrinted { get; set; }

    public NotifyService(SprotoRpc rpc, KcpConnectionManager kcp)
    {
        _rpc = rpc;
        _kcp = kcp;
    }

    /// <summary>
    /// 处理分发器推送的已解码 sproto 消息。
    /// <c>notify.request</c> → 打印通知内容，回复 <c>copy=true</c>。
    /// </summary>
    public void OnPush(RpcMessage msg)
    {
        if (msg.type == "request" && msg.proto == "notify")
        {
            var messageObj = msg.request?.Get("message");
            string? message = messageObj == null ? null : (string)messageObj;
            Console.WriteLine($"\n[NOTIFY] {message ?? "(空消息)"}");

            // 回调：重绘提示符
            OnNotifyPrinted?.Invoke();

            // 回复 copy=true 确认收到
            var resp = _rpc.C2S.NewSprotoObject("notify.response");
            resp["copy"] = true;
            var pkg = _rpc.PackResponse("notify", resp, msg.session, null);

            byte[] buf = new byte[pkg.size];
            Array.Copy(pkg.data, buf, pkg.size);
            _ = _kcp.SendRawAsync(buf, CancellationToken.None);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
