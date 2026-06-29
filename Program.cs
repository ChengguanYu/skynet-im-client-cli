using Im.Cli;
using Im.Config;
using Im.Connection;
using Sproto;

namespace Im;

/// <summary>
/// 应用程序入口点。组装各模块并启动命令循环。
/// </summary>
public static class Program
{
    /// <summary>
    /// 主入口函数。加载配置、创建模块、启动 CLI。
    /// </summary>
    public static async Task Main(string[] args)
    {
        // 加载配置
        AppConfig config = EnvLoader.Load();

        // 创建模块
        using var kcp = new KcpConnectionManager();

        // 加载 sproto schema 并创建 RPC 实例
        string protoPath = Path.Combine(AppContext.BaseDirectory, "Protocol", "proto.sproto");
        SprotoMgr mgr = SprotoParser.ParseFile(protoPath);
        SprotoRpc rpc = new SprotoRpc(mgr, mgr);

        // 共享的 RPC 会话 ID 生成器
        long nextSession = 1;
        using var tcp = new TcpSessionManager(rpc, () => nextSession++);

        using var dispatcher = new KcpRpcDispatcher(rpc, kcp);

        using var commandHandler = new CommandHandler(kcp, tcp, config, rpc, dispatcher);

        // 创建通知服务
        using var notifyService = new NotifyService(rpc, kcp);
        dispatcher.PushReceived += notifyService.OnPush;

        kcp.ConnectionLost += () =>
            Console.WriteLine("\n[INFO] 连接丢失。");

        // 打印横幅
        CommandHandler.PrintBanner();

        // 设置取消令牌（Ctrl+C）
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[INFO] 检测到 Ctrl+C，正在关闭...");
            cts.Cancel();
        };

        // 运行命令循环
        await RunCommandLoopAsync(commandHandler, cts.Token);
    }

    /// <summary>
    /// 主交互命令循环。读取用户输入并交给 CommandHandler 处理。
    /// </summary>
    private static async Task RunCommandLoopAsync(CommandHandler handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Console.Write(handler.GetPrompt());

            string? input;
            try
            {
                input = await Task.Run(() => Console.ReadLine(), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (input is null)
                break;

            try
            {
                bool shouldContinue = await handler.ProcessCommandAsync(input, ct);
                if (!shouldContinue)
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 命令执行失败：{ex.Message}");
            }
        }

        Console.WriteLine("[INFO] 关闭完成。");
    }
}
