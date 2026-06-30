using Im.Connection;
using Sproto;

namespace Im.Cli;

public static class SayCommand
{
    public static async Task ExecuteAsync(SprotoRpc rpc, KcpRpcDispatcher dispatcher, TcpSessionManager tcp, string content, CancellationToken ct)
    {
        try
        {
            var req = rpc.C2S.NewSprotoObject("room_message_push.request");
            req["message"] = content;
            req["msg_code"] = (long)MsgCode.MessagePush;

            var userObj = rpc.C2S.NewSprotoObject("user");
            userObj["account"] = tcp.Account ?? "";
            userObj["name"] = tcp.DisplayName ?? "";
            req["user"] = userObj;

            RpcMessage msg = await dispatcher.SendRequestAsync("room_message_push", req, ct);

            if (msg.response != null)
            {
                var okObj = msg.response.Get("ok");
                bool ok = okObj != null && (bool)okObj;
                if (!ok)
                {
                    var msgObj = msg.response.Get("message");
                    string? errMsg = msgObj == null ? null : (string)msgObj;
                    Console.WriteLine($"[ERROR] {errMsg ?? "发送失败"}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] 发送超时");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 发送失败：{ex.Message}");
        }
    }
}
