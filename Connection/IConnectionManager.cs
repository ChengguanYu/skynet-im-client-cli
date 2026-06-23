using Im.Config;

namespace Im.Connection;

/// <summary>
/// KCP 连接管理器抽象接口。
/// 定义连接生命周期与消息收发的契约。
/// </summary>
public interface IConnectionManager : IDisposable
{
    /// <summary>
    /// 获取当前是否已建立连接。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 收到远程主机消息时触发。
    /// </summary>
    event Action<string>? MessageReceived;

    /// <summary>
    /// 连接意外断开时触发。
    /// </summary>
    event Action? ConnectionLost;

    /// <summary>
    /// 建立到指定主机的 KCP 连接。
    /// </summary>
    Task<bool> ConnectAsync(AppConfig config, CancellationToken ct);

    /// <summary>
    /// 断开远程连接并清理资源。
    /// </summary>
    void Disconnect();

    /// <summary>
    /// 通过 KCP 连接发送文本消息。
    /// </summary>
    Task<bool> SendMessageAsync(string message, CancellationToken ct);
}
