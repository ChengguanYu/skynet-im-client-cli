# Room Create Auto-Entry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `room create -n xxx` 成功后自动执行 `room entry <room_id>`，支持 `--no-entry` 选项跳过自动进入。

**Architecture:** 仅修改 `Cli/RoomCommand.cs` 一个文件。`CreateRoomAsync` 返回类型从 `Task` 改为 `Task<long>` 以传递 room_id，`create` case 中检测 `--no-entry` 标记，room_id >= 0 且无 `--no-entry` 时调用 `EntryRoomAsync`。

**Tech Stack:** C# (.NET 10), SprotoRpc

## Global Constraints

- 保持现有代码风格和错误处理模式
- 不改变任何 RPC 协议逻辑
- 所有 Console.WriteLine 保持现有格式

---

### Task 1: 修改 CreateRoomAsync 返回 room_id

**Files:**
- Modify: `Cli/RoomCommand.cs:86-129`

**Interfaces:**
- Consumes: 无
- Produces: `CreateRoomAsync` 签名变为 `private static async Task<long> CreateRoomAsync(...)`，成功返回 room_id，失败返回 -1

- [ ] **Step 1: 修改 CreateRoomAsync 返回类型和数据流**

修改 `CreateRoomAsync` 方法，将返回类型从 `Task` 改为 `Task<long>`，在每个退出路径返回 room_id（成功）或 -1（失败）：

```csharp
private static async Task<long> CreateRoomAsync(SprotoRpc rpc, TcpSessionManager tcp, string roomName, CancellationToken ct)
{
    string? token = tcp.Token;
    if (string.IsNullOrEmpty(token))
    {
        Console.WriteLine("[ERROR] 创建房间失败：未登录（无 token），room_id=-1");
        return -1;
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
            return -1;
        }

        var roomIdObj = msg.response.Get("room_id");
        long roomId = roomIdObj == null ? -1 : (long)roomIdObj;

        if (roomId < 0)
        {
            Console.WriteLine($"[ERROR] 创建房间失败：room_id={roomId}");
            return -1;
        }

        Console.WriteLine($"[OK] 创建房间成功：room_id={roomId}");
        return roomId;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("[ERROR] 创建房间失败：请求超时（服务端无响应），room_id=-1");
        return -1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] 创建房间失败：{ex.Message}，room_id=-1");
        return -1;
    }
}
```

- [ ] **Step 2: 验证编译**

Run:
```bash
dotnet build 2>&1 | tail -20
```
Expected: 构建成功，无错误

- [ ] **Step 3: Commit**

```bash
git add Cli/RoomCommand.cs
git commit -m "refactor: CreateRoomAsync 返回 room_id 替代 void"
```

### Task 2: create case 中添加 --no-entry 支持和自动 entry

**Files:**
- Modify: `Cli/RoomCommand.cs:60-68`

**Interfaces:**
- Consumes: `CreateRoomAsync` 返回 `long`；`EntryRoomAsync` 现有签名
- Produces: `room create -n xxx` 后自动进入房间

- [ ] **Step 1: 修改 create case**

将 `ExecuteAsync` 中 `create` case 代码从：

```csharp
case "create":
    string? roomName = ParseNameOption(arg);
    if (string.IsNullOrEmpty(roomName))
    {
        Console.WriteLine("用法: room create -n <name>");
        return true;
    }
    await CreateRoomAsync(rpc, tcp, roomName, ct);
    return true;
```

改为：

```csharp
case "create":
    bool noEntry = arg.Contains("--no-entry");
    string? roomName = ParseNameOption(arg);
    if (string.IsNullOrEmpty(roomName))
    {
        Console.WriteLine("用法: room create -n <name>");
        return true;
    }
    long roomId = await CreateRoomAsync(rpc, tcp, roomName, ct);
    if (roomId >= 0 && !noEntry)
    {
        await EntryRoomAsync(rpc, tcp, kcp, dispatcher, roomId, ct, onRoomEntered);
    }
    return true;
```

- [ ] **Step 2: 验证编译**

Run:
```bash
dotnet build 2>&1 | tail -20
```
Expected: 构建成功，无错误

- [ ] **Step 3: Commit**

```bash
git add Cli/RoomCommand.cs
git commit -m "feat: room create 成功后自动执行 room entry，支持 --no-entry 跳过"
```
