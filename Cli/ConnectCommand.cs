using Im.Config;
using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// 处理 connect 指令：TCP 连接 → 构造 login 请求 → 解析响应 → SetAuthenticated → 启动 keepAlive。
/// </summary>
public static class ConnectCommand
{
    /// <summary>
    /// 执行 connect 指令。
    /// </summary>
    /// <param name="config">应用程序配置。</param>
    /// <param name="rpc">sproto RPC 实例，用于构造 login 请求。</param>
    /// <param name="tcp">TCP 会话状态机。</param>
    /// <param name="keepAlive">keepAlive 服务，登录成功后启动。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task ExecuteAsync(AppConfig config, SprotoRpc rpc, TcpSessionManager tcp, KeepAliveService keepAlive, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[INFO] 正在连接 TCP {config.Host}:{config.TcpPort}...");
            await tcp.ConnectAsync(config.Host, config.TcpPort, ct);

            Console.WriteLine($"[OK] TCP 连接成功，正在登录...");
            var req = rpc.C2S.NewSprotoObject("login.request");
            req["account"] = config.Account;
            req["password"] = config.Password;

            RpcMessage msg = await tcp.SendRequestAsync("login", req, ct);

            if (msg.response == null)
            {
                Console.WriteLine("[ERROR] 登录失败：服务端无响应。");
                tcp.Disconnect();
                return;
            }

            var tokenObj = msg.response.Get("token");
            string token = tokenObj == null ? "" : (string)tokenObj;
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("[ERROR] 登录失败：服务端未返回有效 token，请检查账号和密码。");
                tcp.Disconnect();
                return;
            }

            var nameObj = msg.response.Get("name");
            string name = nameObj == null ? "" : (string)nameObj;

            var sessionIdObj = msg.response.Get("session_id");
            long? sessionId = sessionIdObj == null ? null : (long)sessionIdObj;

            // 业务层解析完毕，交给状态机保存登录态并转 Authenticated
            tcp.SetAuthenticated(token, sessionId, name, config.Account);
            Console.WriteLine("[OK] 登录成功");
            if (!string.IsNullOrEmpty(name)) Console.WriteLine($"[INFO] 欢迎 {name}");

            // 登录成功后启动 keepAlive 保活
            keepAlive.Start();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] TCP 连接超时（服务端无响应）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
        }
    }
}
