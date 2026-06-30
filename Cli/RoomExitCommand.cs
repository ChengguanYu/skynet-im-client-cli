using Im.Connection;
using Sproto;

namespace Im.Cli;

public static class RoomExitCommand
{
    public static async Task<bool> ExecuteAsync(SprotoRpc rpc, KcpRpcDispatcher dispatcher, CancellationToken ct)
    {
        try
        {
            var req = rpc.C2S.NewSprotoObject("room_message_push.request");
            req["msg_code"] = (long)MsgCode.RoomExit;

            RpcMessage msg = await dispatcher.SendRequestAsync("room_message_push", req, ct);

            if (msg.response == null)
            {
                Console.WriteLine("[ERROR] 退出房间失败：服务端无响应");
                return false;
            }

            var okObj = msg.response.Get("ok");
            bool ok = okObj != null && (bool)okObj;
            if (ok)
                return true;

            var msgObj = msg.response.Get("message");
            string? errMsg = msgObj == null ? null : (string)msgObj;
            Console.WriteLine($"[ERROR] {errMsg ?? "退出房间失败"}");
            return false;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] 退出房间超时");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 退出房间失败：{ex.Message}");
            return false;
        }
    }
}
