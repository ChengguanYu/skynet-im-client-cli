using Im.Config;
using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// 处理 CLI 命令解析与分发。
/// 在用户输入和连接管理器之间进行协调。
/// </summary>
public sealed class CommandHandler
{
    private readonly IConnectionManager _connectionManager;
    private readonly AppConfig _config;
    private readonly SprotoRpc _rpc;
    private long _nextSession = 1;

    /// <summary>
    /// 初始化 CommandHandler 实例。
    /// </summary>
    /// <param name="connectionManager">KCP 连接管理器。</param>
    /// <param name="config">应用程序配置。</param>
    /// <param name="rpc">sproto RPC 实例，用于请求打包和响应解包。</param>
    public CommandHandler(IConnectionManager connectionManager, AppConfig config, SprotoRpc rpc)
    {
        _connectionManager = connectionManager;
        _config = config;
        _rpc = rpc;
    }

    /// <summary>
    /// 处理单行命令输入。
    /// </summary>
    /// <param name="input">原始用户输入。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>继续运行返回 true，退出返回 false。</returns>
    public async Task<bool> ProcessCommandAsync(string input, CancellationToken ct)
    {
        // 转小写便于命令匹配（.NET Trim 会自动去除 BOM）
        string command = input.Trim().ToLowerInvariant();

        switch (command)
        {
            case "":
                return true;

            case "help":
                PrintHelp();
                return true;

            case "entry":
                await _connectionManager.ConnectAsync(_config, ct);
                return true;

            case "connect":
                return await ConnectCommand.ExecuteAsync(_config, _rpc, () => _nextSession++, ct);

            case "register":
                return await RegisterCommand.ExecuteAsync(_config, _rpc, () => _nextSession++, ct);

            case "disconnect":
                _connectionManager.Disconnect();
                return true;

            case "status":
                PrintStatus();
                return true;

            case "config":
                PrintConfig();
                return true;

            case "quit":
            case "exit":
                _connectionManager.Disconnect();
                Console.WriteLine("[INFO] 再见。");
                return false;

            default:
                if (command.StartsWith("send "))
                {
                    string message = input.Trim()[5..];
                    await _connectionManager.SendMessageAsync(message, ct);
                }
                else
                {
                    Console.WriteLine($"[ERROR] 未知命令：'{command}'，输入 'help' 查看可用命令。");
                }
                return true;
        }
    }

    /// <summary>
    /// 根据当前连接状态获取提示符。
    /// </summary>
    public string GetPrompt()
    {
        return _connectionManager.IsConnected ? "[connected]> " : "[disconnected]> ";
    }

    /// <summary>
    /// 打印应用程序横幅。
    /// </summary>
    public static void PrintBanner()
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("  KCP 协议 CLI 客户端");
        Console.WriteLine("==============================================");
        Console.WriteLine("输入 'help' 查看可用命令。");
        Console.WriteLine();
    }

    /// <summary>
    /// 打印当前连接状态。
    /// </summary>
    private void PrintStatus()
    {
        if (_connectionManager.IsConnected)
            Console.WriteLine($"[STATUS] 已连接到 {_config.Host}:{_config.Port}（会话 ID：0x{_config.Conv:x8}）");
        else
            Console.WriteLine("[STATUS] 未连接");
    }

    /// <summary>
    /// 打印当前配置。
    /// </summary>
    private void PrintConfig()
    {
        Console.WriteLine($"  HOST     = {_config.Host}");
        Console.WriteLine($"  PORT     = {_config.Port}");
        Console.WriteLine($"  TCP_PORT = {_config.TcpPort}");
        Console.WriteLine($"  ACCOUNT  = {_config.Account}");
        Console.WriteLine($"  CONV     = 0x{_config.Conv:x8} ({_config.Conv})");
    }

    /// <summary>
    /// 打印帮助信息。
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine("可用命令：");
        Console.WriteLine("  entry            - 通过 KCP 连接到远程服务器");
        Console.WriteLine("  connect          - 通过 TCP 连接远程服务器并发送 login");
        Console.WriteLine("  register         - 注册新账号（输入密码 → 确认密码 → 确认注册 → 调用 TCP）");
        Console.WriteLine("  disconnect       - 断开远程连接");
        Console.WriteLine("  send <message>   - 发送文本消息到服务器");
        Console.WriteLine("  status           - 查看当前连接状态");
        Console.WriteLine("  config           - 查看当前 HOST/PORT 配置");
        Console.WriteLine("  help             - 显示此帮助信息");
        Console.WriteLine("  quit / exit      - 断开并退出程序");
        Console.WriteLine();
        Console.WriteLine("按 Ctrl+C 可随时强制退出。");
    }
}
