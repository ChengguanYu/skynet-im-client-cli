using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// 房间命令：room list / room entry &lt;room&gt; / room create -n &lt;name&gt;。
/// </summary>
public static class RoomCommand
{
    /// <summary>
    /// 执行 room 子命令。
    /// </summary>
    /// <param name="rpc">sproto RPC 实例，用于构造 create_room 等业务请求。</param>
    /// <param name="tcp">TCP 会话状态机，用于检查登录态与收发请求。</param>
    /// <param name="input">原始用户输入（以 room 开头）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>始终返回 true，不退出程序。</returns>
    public static async Task<bool> ExecuteAsync(SprotoRpc rpc, TcpSessionManager tcp, string input, CancellationToken ct)
    {
        if (!tcp.IsLoggedIn)
        {
            Console.WriteLine("[ERROR] 请先 connect 登录");
            return true;
        }

        string trimmed = input.Trim();
        string rest = trimmed.Length >= 4 ? trimmed[4..].Trim() : "";

        if (string.IsNullOrEmpty(rest))
        {
            PrintUsage();
            return true;
        }

        int spaceIdx = rest.IndexOf(' ');
        string sub = (spaceIdx < 0 ? rest : rest[..spaceIdx]).ToLowerInvariant();
        string arg = spaceIdx < 0 ? "" : rest[(spaceIdx + 1)..].Trim();

        switch (sub)
        {
            case "list":
                Console.WriteLine("[INFO] room list：列出房间列表（业务待实现）");
                return true;

            case "entry":
                if (string.IsNullOrEmpty(arg))
                {
                    Console.WriteLine("用法: room entry <room>");
                    return true;
                }
                Console.WriteLine($"[INFO] room entry {arg}：进入房间（业务待实现）");
                return true;

            case "create":
                string? roomName = ParseNameOption(arg);
                if (string.IsNullOrEmpty(roomName))
                {
                    Console.WriteLine("用法: room create -n <name>");
                    return true;
                }
                await CreateRoomAsync(rpc, tcp, roomName, ct);
                return true;

            default:
                PrintUsage();
                return true;
        }
    }

    /// <summary>
    /// 构造并发送 create_room 请求。
    /// proto: create_room.request { token 0 : string; room_name 1 : string }
    ///        create_room.response { room_id 0 : integer }
    /// 成功打印 room_id；失败通知并打印 room_id=-1。
    /// </summary>
    /// <param name="rpc">sproto RPC 实例，用于构造 create_room 请求。</param>
    /// <param name="tcp">TCP 会话状态机，提供 token 与通用收发。</param>
    /// <param name="roomName">房间名称。</param>
    /// <param name="ct">取消令牌。</param>
    private static async Task CreateRoomAsync(SprotoRpc rpc, TcpSessionManager tcp, string roomName, CancellationToken ct)
    {
        string? token = tcp.Token;
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("[ERROR] 创建房间失败：未登录（无 token），room_id=-1");
            return;
        }

        try
        {
            var req = rpc.C2S.NewSprotoObject("create_room.request");
            req["token"] = token;
            req["room_name"] = roomName;

            Console.WriteLine($"[INFO] 正在创建房间 '{roomName}'...");
            RpcMessage msg = await tcp.SendRequestAsync("create_room", req, ct);

            if (msg.response == null)
            {
                Console.WriteLine("[ERROR] 创建房间失败：服务端无响应，room_id=-1");
                return;
            }

            var roomIdObj = msg.response.Get("room_id");
            long roomId = roomIdObj == null ? -1 : (long)roomIdObj;

            if (roomId < 0)
            {
                Console.WriteLine($"[ERROR] 创建房间失败：room_id={roomId}");
                return;
            }

            Console.WriteLine($"[OK] 创建房间成功：room_id={roomId}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] 创建房间失败：请求超时（服务端无响应），room_id=-1");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 创建房间失败：{ex.Message}，room_id=-1");
        }
    }

    /// <summary>
    /// 从参数中解析 -n 选项的值，保留原始大小写。
    /// </summary>
    private static string? ParseNameOption(string arg)
    {
        var tokens = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "-n" && i + 1 < tokens.Length)
                return tokens[i + 1];
        }
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法: room list | room entry <room> | room create -n <name>");
    }
}
