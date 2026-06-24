using Im.Config;
using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// 处理 register 指令：交互输入 → TCP 连接 → 构造 register 请求 → 解析响应。
/// 注册不改变状态机的 Authenticated 状态。
/// </summary>
public static class RegisterCommand
{
    /// <summary>
    /// 执行 register 指令。
    /// </summary>
    /// <param name="config">应用程序配置。</param>
    /// <param name="rpc">sproto RPC 实例，用于构造 register 请求。</param>
    /// <param name="tcp">TCP 会话状态机。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task ExecuteAsync(AppConfig config, SprotoRpc rpc, TcpSessionManager tcp, CancellationToken ct)
    {
        Console.Write("请输入昵称: ");
        string? name = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("[ERROR] 昵称不能为空");
            return;
        }

        Console.Write("请输入密码: ");
        string? pw1 = Console.ReadLine();
        Console.Write("请重复密码: ");
        string? pw2 = Console.ReadLine();

        if (string.IsNullOrEmpty(pw1) || pw1 != pw2)
        {
            Console.WriteLine("[ERROR] 两次密码不一致或密码为空");
            return;
        }

        Console.Write("确认注册？(y/n): ");
        string? confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("[INFO] 已取消注册");
            return;
        }

        try
        {
            Console.WriteLine($"[INFO] 正在向 {config.Host}:{config.TcpPort} 发送注册请求...");
            await tcp.ConnectAsync(config.Host, config.TcpPort, ct);

            var req = rpc.C2S.NewSprotoObject("register.request");
            req["name"] = name;
            req["password"] = pw1;

            RpcMessage msg = await tcp.SendRequestAsync("register", req, ct);

            if (msg.response != null)
            {
                var accountObj = msg.response.Get("account");
                if (accountObj != null)
                    Console.WriteLine($"[OK] 注册成功，账号={(string)accountObj}");
                else
                    Console.WriteLine("[ERROR] 注册失败：服务端未返回账号");
            }
            else
            {
                Console.WriteLine("[ERROR] 注册失败：服务端无响应");
            }

            // 注册完成，断开 TCP（注册不需要保持连接）
            tcp.Disconnect();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] 注册请求超时（服务端无响应）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 注册失败：{ex.Message}");
        }
    }
}
