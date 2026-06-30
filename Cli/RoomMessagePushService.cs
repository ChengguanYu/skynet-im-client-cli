using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// KCP 房间消息推送服务。处理服务端推送的 <c>room_message_push</c>，
/// 打印消息并回复 <c>ok=true</c>。
/// 输入来自 <see cref="KcpRpcDispatcher.PushReceived"/> 事件。
/// </summary>
public sealed class RoomMessagePushService : IDisposable
{
    private readonly SprotoRpc _rpc;
    private readonly KcpConnectionManager _kcp;
    private bool _disposed;

    /// <summary>
    /// 消息打印后回调，用于重绘提示符。
    /// </summary>
    public Action? OnMessagePrinted { get; set; }

    /// <summary>
    /// 初始化 <see cref="RoomMessagePushService"/> 实例。
    /// </summary>
    /// <param name="rpc">SprotoRpc 实例，用于构造响应包。</param>
    /// <param name="kcp">KCP 连接管理器，用于发送响应数据。</param>
    public RoomMessagePushService(SprotoRpc rpc, KcpConnectionManager kcp)
    {
        _rpc = rpc;
        _kcp = kcp;
    }

    /// <summary>
    /// 处理分发器推送的已解码 sproto 消息。
    /// <c>room_message_push</c> → 打印消息内容，回复 <c>ok=true</c>。
    /// </summary>
    /// <param name="msg">已解码的 RPC 消息对象，包含 type、proto、request 及 session 信息。</param>
    public void OnPush(RpcMessage msg)
    {
        if (msg.type != "request" || msg.proto != "room_message_push")
            return;

        // 提取 user 字段
        string? userName = null;
        string? userAccount = null;
        var userObj = msg.request?.Get("user");
        if (userObj is SprotoObject u)
        {
            var nameObj = u.Get("name");
            userName = nameObj == null ? null : (string)nameObj;

            var accountObj = u.Get("account");
            userAccount = accountObj == null ? null : (string)accountObj;
        }

        // 提取 message
        var messageObj = msg.request?.Get("message");
        string? message = messageObj == null ? null : (string)messageObj;

        // 打印：user.name<user.account> ： message
        Console.WriteLine($"{userName ?? "?"}<{userAccount ?? "?"}> ： {message ?? "(空消息)"}");

        // 回调：重绘提示符
        OnMessagePrinted?.Invoke();

        // 回复 ok=true
        var resp = _rpc.C2S.NewSprotoObject("room_message_push.response");
        resp["ok"] = true;
        var pkg = _rpc.PackResponse("room_message_push", resp, msg.session, null);

        byte[] buf = new byte[pkg.size];
        Array.Copy(pkg.data, buf, pkg.size);
        _ = _kcp.SendRawAsync(buf, CancellationToken.None);
    }

    /// <summary>
    /// 释放资源。幂等实现。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
