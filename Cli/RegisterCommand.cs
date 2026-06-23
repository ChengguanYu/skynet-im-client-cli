using System.Net.Sockets;
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

        // 调用 TCP 接口
        try
        {
            Console.WriteLine($"[INFO] 正在向 {config.Host}:{config.TcpPort} 发送注册请求...");
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(config.Host, config.TcpPort, cts.Token);

            var registerReq = rpc.C2S.NewSprotoObject("register.request");
            registerReq["password"] = pw1;

            long session = nextSession();
            RpcPackage pkg = rpc.PackRequest("register", registerReq, session);

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

            RpcMessage msg = rpc.UnpackMessage(packedResp, respLen);
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
