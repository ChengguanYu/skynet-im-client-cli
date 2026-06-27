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
    public static async Task<bool> ExecuteAsync(SprotoRpc rpc, TcpSessionManager tcp, KcpConnectionManager kcp, string input, CancellationToken ct)
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
                await ListRoomsAsync(rpc, tcp, ct);
                return true;

            case "entry":
                if (string.IsNullOrEmpty(arg))
                {
                    Console.WriteLine("用法: room entry <roomID>");
                    return true;
                }
                if (!long.TryParse(arg, out long roomId))
                {
                    Console.WriteLine("[ERROR] roomID 须为整数");
                    return true;
                }
                await EntryRoomAsync(rpc, tcp, kcp, roomId, ct);
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
    /// 构造并发送 get_rooms 请求，格式化打印房间列表。
    /// proto: get_rooms.request { token 0 : string }
    ///        get_rooms.response { rooms 0 : room * }
    /// 成功打印房间列表；失败或空则提示"当前无房间"。
    /// </summary>
    private static async Task ListRoomsAsync(SprotoRpc rpc, TcpSessionManager tcp, CancellationToken ct)
    {
        string? token = tcp.Token;
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("当前无房间");
            return;
        }

        try
        {
            var req = rpc.C2S.NewSprotoObject("get_rooms.request");
            req["token"] = token;

            RpcMessage msg = await tcp.SendRequestAsync("get_rooms", req, ct);

            if (msg.response == null)
            {
                Console.WriteLine("当前无房间");
                return;
            }

            var roomsObj = msg.response.Get("rooms");
            if (roomsObj == null)
            {
                Console.WriteLine("当前无房间");
                return;
            }

            List<SprotoObject> rooms;
            try
            {
                rooms = (List<SprotoObject>)roomsObj;
            }
            catch
            {
                Console.WriteLine("当前无房间");
                return;
            }

            if (rooms.Count == 0)
            {
                Console.WriteLine("当前无房间");
                return;
            }

            Console.WriteLine($"房间列表（共 {rooms.Count} 个）：");
            Console.WriteLine($"  {"ID",6}  {"名称",-20}  创建者");
            Console.WriteLine($"  {new string('-', 6)}  {new string('-', 20)}  {new string('-', 10)}");
            foreach (var room in rooms)
            {
                long roomId = (long)room.Get("room_id");
                string roomName = (string)room.Get("room_name");
                string roomOwner = (string)room.Get("room_owner");
                Console.WriteLine($"  {roomId,6}  {roomName,-20}  {roomOwner}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] 获取房间列表失败：请求超时（服务端无响应）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 获取房间列表失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 进入房间完整流程：TCP → create_kcp_session → 建立 KCP → KCP → activate_kcp_session。
    /// proto: create_kcp_session.response { ok 0 : boolean; kcp_conv 1 : integer; kcp_address 2 : string; session_challenge_code 3 : string }
    ///        activate_kcp_session.request { token 0 : string; conv 1 : integer }
    ///        activate_kcp_session.response { ok 0 : boolean }
    /// </summary>
    private static async Task EntryRoomAsync(SprotoRpc rpc, TcpSessionManager tcp, KcpConnectionManager kcp, long roomId, CancellationToken ct)
    {
        string? token = tcp.Token;
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("[ERROR] 进入房间失败：未登录（无 token）");
            return;
        }

        try
        {
            // === Phase 1: TCP → create_kcp_session ===
            var req = rpc.C2S.NewSprotoObject("create_kcp_session.request");
            req["token"] = token;
            req["room_id"] = roomId;

            Console.WriteLine($"[INFO] 正在进入房间 {roomId}...");
            RpcMessage createMsg = await tcp.SendRequestAsync("create_kcp_session", req, ct);

            if (createMsg.response == null)
            {
                Console.WriteLine("[ERROR] 进入房间失败：服务端无响应");
                return;
            }

            var okObj = createMsg.response.Get("ok");
            bool ok = okObj != null && (bool)okObj;

            if (!ok)
            {
                Console.WriteLine("[ERROR] 建立会话失败：房间不存在");
                return;
            }

            var kcpConvObj = createMsg.response.Get("kcp_conv");
            long kcpConvLong = kcpConvObj == null ? -1 : (long)kcpConvObj;
            if (kcpConvLong < 0)
            {
                Console.WriteLine("[ERROR] 进入房间失败：服务端未返回有效的 kcp_conv");
                return;
            }
            uint kcpConv = (uint)kcpConvLong;

            string kcpAddress = (string)createMsg.response.Get("kcp_address") ?? "";
            if (string.IsNullOrEmpty(kcpAddress))
            {
                Console.WriteLine("[ERROR] 进入房间失败：服务端未返回 kcp_address");
                return;
            }

            Console.WriteLine($"[INFO] create_kcp_session 成功：conv=0x{kcpConv:x8}, address={kcpAddress}");

            // === Phase 2: 建立 KCP 连接到服务器的 KCP 端口 ===
            kcp.Disconnect();

            string[] addrParts = kcpAddress.Split(':');
            if (addrParts.Length != 2 || !int.TryParse(addrParts[1], out int kcpPort))
            {
                Console.WriteLine("[ERROR] 进入房间失败：kcp_address 格式无效");
                return;
            }

            bool kcpConnected = await kcp.ConnectToAsync(addrParts[0], kcpPort, kcpConv, ct);
            if (!kcpConnected)
            {
                Console.WriteLine("[ERROR] 进入房间失败：KCP 连接失败");
                return;
            }

            // === Phase 3: KCP → activate_kcp_session ===
            var activateReq = rpc.C2S.NewSprotoObject("activate_kcp_session.request");
            activateReq["token"] = token;
            activateReq["conv"] = kcpConvLong;

            Console.WriteLine("[INFO] 正在通过 KCP 发送 activate_kcp_session...");
            long sessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            RpcPackage activatePkg = rpc.PackRequest("activate_kcp_session", activateReq, sessionId);

            byte[] sendBuf = new byte[activatePkg.size];
            Array.Copy(activatePkg.data, sendBuf, activatePkg.size);

            bool sent = await kcp.SendRawAsync(sendBuf, ct);
            if (!sent)
            {
                Console.WriteLine("[ERROR] 进入房间失败：activate_kcp_session 发送失败");
                return;
            }

            Console.WriteLine("[INFO] 等待 activate_kcp_session 响应...");
            byte[] responseBytes = await kcp.WaitForRawResponseAsync(ct);

            RpcMessage activateMsg = rpc.UnpackMessage(responseBytes, responseBytes.Length);
            if (activateMsg.response == null)
            {
                Console.WriteLine("[ERROR] 进入房间失败：激活 KCP 会话无响应");
                return;
            }

            var activateOkObj = activateMsg.response.Get("ok");
            bool activateOk = activateOkObj != null && (bool)activateOkObj;

            if (!activateOk)
            {
                Console.WriteLine("[ERROR] 进入房间失败：服务端拒绝激活 KCP 会话");
                return;
            }

            Console.WriteLine($"[OK] 进入房间成功：room_id={roomId}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[ERROR] 进入房间失败：请求超时（服务端无响应）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 进入房间失败：{ex.Message}");
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
