namespace Im.Config;

/// <summary>
/// 应用程序配置，从 .env 文件加载。
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// 远程服务器主机地址（IPv4）。
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// 远程服务器端口号（1-65535）。
    /// </summary>
    public int Port { get; set; } = 12345;

    /// <summary>
    /// 远程服务器 TCP 端口号（connect 指令使用）。
    /// </summary>
    public int TcpPort { get; set; } = 8889;

    /// <summary>
    /// KCP 会话 ID。由客户端自定，服务端按 conv 路由会话。
    /// </summary>
    /// <summary>
    /// 登录账号。
    /// </summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>
    /// 登录密码。
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// KCP 会话 ID。由客户端自定，服务端按 conv 路由会话。
    /// </summary>
    public uint Conv { get; set; } = 0x11223344;
}
