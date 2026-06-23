using System.Net.Sockets;
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
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(config.Host, config.TcpPort, cts.Token);

            // 构建 login 请求
            var loginReq = rpc.C2S.NewSprotoObject("login.request");
            loginReq["account"] = config.Account;
            loginReq["password"] = config.Password;

            // 打包请求（含 .package 头部 + body + sproto_pack）
            long session = nextSession();
            RpcPackage pkg = rpc.PackRequest("login", loginReq, session);

            // 发送：2 字节大端长度前缀 + packed 数据
            byte[] packet = new byte[2 + pkg.size];
            packet[0] = (byte)((pkg.size >> 8) & 0xFF);
            packet[1] = (byte)(pkg.size & 0xFF);
            Array.Copy(pkg.data, 0, packet, 2, pkg.size);
            await tcp.GetStream().WriteAsync(packet, cts.Token);

            // 读取响应
            byte[] lenBuf = new byte[2];
            await tcp.GetStream().ReadExactlyAsync(lenBuf, 0, 2, cts.Token);
            int respLen = (lenBuf[0] << 8) | lenBuf[1];

            byte[] packedResp = new byte[respLen];
            await tcp.GetStream().ReadExactlyAsync(packedResp, 0, respLen, cts.Token);

            // 解包响应
            RpcMessage msg = rpc.UnpackMessage(packedResp, respLen);
            Console.WriteLine($"[OK] TCP 连接成功，type={msg.type} session={msg.session} proto={msg.proto}");

            // 解码响应字段
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
