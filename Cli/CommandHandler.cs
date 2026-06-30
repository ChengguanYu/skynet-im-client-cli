using Im.Config;
using Im.Connection;
using Sproto;

namespace Im.Cli;

public enum PromptState
{
    Disconnected,
    Connected,
    InRoom,
}

/// <summary>
/// 处理 CLI 命令解析与分发。
/// 在用户输入和连接管理器之间进行协调。
/// </summary>
public sealed class CommandHandler : IDisposable
{
    private readonly KcpConnectionManager _kcp;
    private readonly TcpSessionManager _tcp;
    private readonly KeepAliveService _keepAlive;
    private readonly AppConfig _config;
    private readonly SprotoRpc _rpc;
    private readonly KcpRpcDispatcher _kcpDispatcher;
    private PromptState _promptState = PromptState.Disconnected;
    private string? _currentRoomName;

    /// <summary>
    /// 初始化 CommandHandler 实例。
    /// </summary>
    /// <param name="kcp">KCP 连接管理器，负责 entry/send/disconnect 的 KCP 通道。</param>
    /// <param name="tcp">TCP 会话状态机，负责 TCP 连接生命周期与通用收发。</param>
    /// <param name="config">应用程序配置。</param>
    /// <param name="rpc">sproto RPC 实例，用于构造 login/register 等业务请求。</param>
    /// <param name="kcpDispatcher">KCP 请求-响应分发器，按 session ID 匹配响应。</param>
    public CommandHandler(KcpConnectionManager kcp, TcpSessionManager tcp, AppConfig config, SprotoRpc rpc, KcpRpcDispatcher kcpDispatcher)
    {
        _kcp = kcp;
        _tcp = tcp;
        _config = config;
        _rpc = rpc;
        _kcpDispatcher = kcpDispatcher;
        _keepAlive = new KeepAliveService(rpc, tcp);
        _tcp.ConnectionLost += OnTcpConnectionLost;
        _kcp.ConnectionLost += OnKcpConnectionLost;
    }

    /// <summary>
    /// TCP 连接异常断开：停止 keepAlive 并提示用户。
    /// 登录态已由 TcpSessionManager 内部清理。
    /// </summary>
    private void OnTcpConnectionLost()
    {
        _keepAlive.Stop();
        _promptState = PromptState.Disconnected;
        _currentRoomName = null;
        Console.WriteLine("\n[INFO] 登录已失效，请重新 connect。");
    }

    private void OnKcpConnectionLost()
    {
        if (_promptState == PromptState.InRoom)
        {
            _currentRoomName = null;
            _promptState = PromptState.Connected;
            Console.WriteLine("\n[INFO] 房间连接已断开。");
        }
    }

    /// <summary>
    /// 处理单行命令输入。
    /// </summary>
    public async Task<bool> ProcessCommandAsync(string input, CancellationToken ct)
    {
        string command = input.Trim().ToLowerInvariant();

        if (command == "room" || command.StartsWith("room "))
        {
            return await RoomCommand.ExecuteAsync(_rpc, _tcp, _kcp, _kcpDispatcher, input, ct, onRoomEntered: roomName =>
            {
                _currentRoomName = roomName;
                _promptState = PromptState.InRoom;
            });
        }

        switch (command)
        {
            case "":
                return true;

            case "help":
                PrintHelp();
                return true;

            case "entry":
                try
                {
                    // 1. 建立 KCP 连接
                    bool kcpConnected = await _kcp.ConnectAsync(_config, ct);
                    if (!kcpConnected)
                    {
                        Console.WriteLine("[ERROR] KCP 连接失败");
                        return true;
                    }

                    // 2. 通过 KcpRpcDispatcher 发送 create_kcp_session 请求并等待响应
                    var req = _rpc.C2S.NewSprotoObject("create_kcp_session.request");
                    req["token"] = "mooc";

                    Console.WriteLine("[INFO] 正在通过 KCP 发送 create_kcp_session 请求...");
                    RpcMessage msg = await _kcpDispatcher.SendRequestAsync("create_kcp_session", req, ct);

                    if (msg.response == null)
                    {
                        Console.WriteLine("[ERROR] create_kcp_session 失败：服务端无响应");
                        return true;
                    }

                    var okObj = msg.response.Get("ok");
                    bool ok = okObj != null && (bool)okObj;
                    Console.WriteLine($"[INFO] create_kcp_session 响应：ok={ok}");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[ERROR] create_kcp_session 请求超时");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] create_kcp_session 请求失败：{ex.Message}");
                }
                return true;

            case "connect":
                if (_tcp.IsLoggedIn)
                {
                    Console.WriteLine("[ERROR] 已登录，无需 login");
                    return true;
                }
                await ConnectCommand.ExecuteAsync(_config, _rpc, _tcp, _keepAlive, ct);
                if (_tcp.IsLoggedIn)
                    _promptState = PromptState.Connected;
                return true;

            case "register":
                if (_tcp.IsConnected)
                {
                    Console.WriteLine("[ERROR] TCP 已连接，请先 disconnect");
                    return true;
                }
                await RegisterCommand.ExecuteAsync(_config, _rpc, _tcp, ct);
                return true;

            case "disconnect":
                _keepAlive.Stop();
                _promptState = PromptState.Disconnected;
                _currentRoomName = null;
                _tcp.Disconnect();
                _kcp.Disconnect();
                return true;

            case "status":
                PrintStatus();
                return true;

            case "config":
                PrintConfig();
                return true;

            case "quit":
            case "exit":
                _keepAlive.Dispose();
                _tcp.Dispose();
                _kcp.Disconnect();
                Console.WriteLine("[INFO] 再见。");
                return false;

            default:
                if (command == "say" || command.StartsWith("say "))
                {
                    if (_promptState != PromptState.InRoom)
                    {
                        Console.WriteLine("[ERROR] 请先进入房间");
                        return true;
                    }
                    if (command == "say")
                    {
                        Console.WriteLine("[ERROR] say <message>");
                        return true;
                    }
                    string content = input.Trim()[4..];
                    await SayCommand.ExecuteAsync(_rpc, _kcpDispatcher, _tcp, content, ct);
                }
                else if (command.StartsWith("send "))
                {
                    string message = input.Trim()[5..];
                    await _kcp.SendMessageAsync(message, ct);
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
        switch (_promptState)
        {
            case PromptState.Connected:
                return $"connected {_tcp.DisplayName}@connected> ";
            case PromptState.InRoom:
                return $"InRoom {_tcp.DisplayName}@{_currentRoomName}> ";
            case PromptState.Disconnected:
            default:
                return "disconnected> ";
        }
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
        if (_kcp.IsConnected)
            Console.WriteLine($"[STATUS] KCP 已连接到 {_config.Host}:{_config.Port}（会话 ID：0x{_config.Conv:x8}）");
        else
            Console.WriteLine("[STATUS] KCP 未连接");

        Console.WriteLine($"[STATUS] TCP 状态：{_tcp.State}");
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
    /// 打印帮助信息，仅展示当前状态可用的命令。
    /// </summary>
    private void PrintHelp()
    {
        Console.WriteLine("可用命令：");
        Console.WriteLine("  entry            - 通过 KCP 连接到远程服务器");
        if (!_tcp.IsLoggedIn)
        {
            Console.WriteLine("  connect          - 通过 TCP 连接远程服务器并发送 login");
            Console.WriteLine("  register         - 注册新账号（输入密码 → 确认密码 → 确认注册 → 调用 TCP）");
        }
        else
        {
            Console.WriteLine("  room list        - 列出房间列表（业务待实现）");
            Console.WriteLine("  room entry <room>- 进入指定房间（业务待实现）");
            Console.WriteLine("  room create -n <name> - 创建房间");
        }
        Console.WriteLine("  disconnect       - 断开远程连接");
        Console.WriteLine("  send <message>   - 发送文本消息到服务器");
        Console.WriteLine("  say <message>    - 在房间内发送消息");
        Console.WriteLine("  status           - 查看当前连接状态");
        Console.WriteLine("  config           - 查看当前 HOST/PORT 配置");
        Console.WriteLine("  help             - 显示此帮助信息");
        Console.WriteLine("  quit / exit      - 断开并退出程序");
        Console.WriteLine();
        Console.WriteLine("按 Ctrl+C 可随时强制退出。");
    }

    /// <summary>
    /// 释放 CommandHandler 持有的资源。
    /// </summary>
    public void Dispose()
    {
        _keepAlive.Dispose();
        _tcp.Dispose();
    }
}
