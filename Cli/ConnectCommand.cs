using Im.Config;
using Sproto;

namespace Im.Cli;

/// <summary>
/// 处理 connect 指令：通过 TCP 连接服务端并发送 login 请求。
/// </summary>
/// <param name="onLogin">登录成功回调，参数为 (token, name)，由调用方写回登录态。</param>
public static class ConnectCommand
{
    public static async Task<bool> ExecuteAsync(AppConfig config, SprotoRpc rpc, Func<long> nextSession, CancellationToken ct, Action<string, string>? onLogin = null)
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
                string token = tokenObj == null ? "" : (string)tokenObj;
                if (string.IsNullOrEmpty(token))
                {
                    // token 为空说明登录失败，提醒用户
                    Console.WriteLine("[ERROR] 登录失败：服务端未返回有效 token，请检查账号和密码。");
                    return true;
                }

                var nameObj = msg.response.Get("name");
                string name = nameObj == null ? "" : (string)nameObj;
                onLogin?.Invoke(token, name);
                Console.WriteLine("[OK] 登录成功");
                if (!string.IsNullOrEmpty(name)) Console.WriteLine($"[INFO] 欢迎 {name}");
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
