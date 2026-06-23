using Im.Config;
using Sproto;

namespace Im.Cli;

/// <summary>
/// 处理 connect 指令：通过 TCP 连接服务端并发送 login 请求。
/// </summary>
public static class ConnectCommand
{
    public static async Task<bool> ExecuteAsync(AppConfig config, SprotoRpc rpc, Func<long> nextSession, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[INFO] 正在连接 TCP {config.Host}:{config.TcpPort}...");

            using var session = new TcpSession(rpc);
            await session.ConnectAsync(config.Host, config.TcpPort, ct);

            var loginReq = rpc.C2S.NewSprotoObject("login.request");
            loginReq["account"] = config.Account;
            loginReq["password"] = config.Password;

            RpcMessage msg = await session.SendRequestAsync("login", loginReq, nextSession(), ct);
            Console.WriteLine($"[OK] TCP 连接成功，type={msg.type} session={msg.session} proto={msg.proto}");

            if (msg.response != null)
            {
                var tokenObj = msg.response.Get("token");
                if (tokenObj != null)
                    Console.WriteLine($"[OK] login 响应 token={(string)tokenObj}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] TCP 连接超时（服务端无响应）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] TCP 连接失败：{ex.Message}");
        }
        return true;
    }
}
