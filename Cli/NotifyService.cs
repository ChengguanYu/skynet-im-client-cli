using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// KCP 通知服务。订阅 KCP 通道裸数据，解析 sproto 消息，
/// 处理服务端推送的 <c>notify.request</c>，打印通知并回复 <c>copy=true</c>。
/// </summary>
public sealed class NotifyService : IDisposable
{
    private readonly SprotoRpc _rpc;
    private readonly KcpConnectionManager _kcp;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 初始化通知服务。
    /// </summary>
    /// <param name="rpc">Sproto RPC 实例，用于消息解包和打包。</param>
    /// <param name="kcp">KCP 连接管理器，用于订阅消息事件和发送响应。</param>
    public NotifyService(SprotoRpc rpc, KcpConnectionManager kcp)
    {
        _rpc = rpc;
        _kcp = kcp;
    }

    /// <summary>
    /// 开始监听 KCP 通道上的通知消息。
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _kcp.MessageReceived += OnRawData;
    }

    /// <summary>
    /// 停止监听 KCP 通道上的通知消息。
    /// </summary>
    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _kcp.MessageReceived -= OnRawData;
    }

    /// <summary>
    /// 释放资源，停止监听。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    /// <summary>
    /// KCP 裸数据回调。尝试解析为 sproto 消息：
    /// <list type="bullet">
    ///   <item><c>notify.request</c> → 打印通知内容，回复 <c>copy=true</c></item>
    ///   <item>其他 sproto 消息 → 打印警告</item>
    ///   <item>解析失败 → 打印警告，不中断连接</item>
    /// </list>
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
            Console.WriteLine($"[ERROR] 解析 sproto 失败：{ex.Message}");
            return;
        }

        if (msg.type == "request" && msg.proto == "notify")
        {
            var messageObj = msg.request?.Get("message");
            string? message = messageObj == null ? null : (string)messageObj;
            Console.WriteLine($"\n[NOTIFY] {message ?? "(空消息)"}");

            // 回复 copy=true 确认收到
            var resp = _rpc.C2S.NewSprotoObject("notify.response");
            resp["copy"] = true;
            var pkg = _rpc.PackResponse("notify", resp, msg.session, null);

            byte[] buf = new byte[pkg.size];
            Array.Copy(pkg.data, buf, pkg.size);
            _ = _kcp.SendRawAsync(buf, CancellationToken.None);
        }
        else
        {
            Console.WriteLine($"[WARN] 收到未处理的 sproto 消息：proto={msg.proto}, type={msg.type}");
        }
    }
}
