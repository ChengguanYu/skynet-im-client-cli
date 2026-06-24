using Im.Config;
using Sproto;

namespace Im.Cli;

/// <summary>
/// 处理 register 注册指令：密码输入 → 重复确认 → y/n 确认 → TCP 调用。
/// </summary>
public static class RegisterCommand
{
    public static async Task<bool> ExecuteAsync(AppConfig config, SprotoRpc rpc, Func<long> nextSession, CancellationToken ct)
    {
        Console.Write("请输入昵称: ");
        string? name = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("[ERROR] 昵称不能为空");
            return true;
        }

        Console.Write("请输入密码: ");
        string? pw1 = Console.ReadLine();
        Console.Write("请重复密码: ");
        string? pw2 = Console.ReadLine();

        if (string.IsNullOrEmpty(pw1) || pw1 != pw2)
        {
            Console.WriteLine("[ERROR] 两次密码不一致或密码为空");
            return true;
        }

        Console.Write("确认注册？(y/n): ");
        string? confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("[INFO] 已取消注册");
            return true;
        }

        try
        {
            Console.WriteLine($"[INFO] 正在向 {config.Host}:{config.TcpPort} 发送注册请求...");

            using var session = new TcpSession(rpc);
            await session.ConnectAsync(config.Host, config.TcpPort, ct);

            var registerReq = rpc.C2S.NewSprotoObject("register.request");
            registerReq["name"] = name;
            registerReq["password"] = pw1;

            RpcMessage msg = await session.SendRequestAsync("register", registerReq, nextSession(), ct);
            Console.WriteLine($"[OK] 注册请求已发送，type={msg.type} session={msg.session} proto={msg.proto}");

            // TODO: 后续逻辑先留空
            if (msg.response != null)
            {
                var accountObj = msg.response.Get("account");
                if (accountObj != null)
                    Console.WriteLine($"[OK] 注册成功，账号={(string)accountObj}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] 注册请求超时（服务端无响应）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 注册失败：{ex.Message}");
        }
        return true;
    }
}
