# 提示符状态机：在提示符中显示房间名称

## 概述

在 `room entry` 成功流程中，`create_kcp_session` 响应返回 `room_name`。我们需要先改造 CLI 提示符格式，再在 entry 成功后将该值显示在提示符中。

分两次独立提交完成：
1. **Step 1** — 提示符格式从 `<name>@[connected]> ` 改为 `{name}@{state}> `（无 room 时统一为 `connected`）
2. **Step 2** — entry 成功后保存 `room_name`，提示符变为 `{name}@{room_name}> `

## 状态枚举

```csharp
enum PromptState
{
    Disconnected,  // TCP 未登录
    Connected,     // TCP 已登录，KCP 未连接（未进房间）
    InRoom         // TCP 已登录 + KCP 会话活跃
}
```

### 状态转换

| 事件 | 入口 → 出口 | 说明 |
|------|------------|------|
| 程序启动 | — → `Disconnected` | 初始状态 |
| `connect` 成功 | `Disconnected` → `Connected` | TCP 登录成功 |
| `room entry` 成功 | `Connected` → `InRoom` | 进入房间 + KCP 建立 |
| `disconnect` 命令 | * → `Disconnected` | 手动断开所有连接 |
| TCP 意外断开 | * → `Disconnected` | `OnTcpConnectionLost` |
| KCP 断开但 TCP 仍在 | `InRoom` → `Connected` | 仅 KCP 通道断开 |

## 提示符格式

| PromptState | 提示符 |
|------------|--------|
| `Disconnected` | `disconnected> ` |
| `Connected` | `{DisplayName}@connected> ` |
| `InRoom` | `{DisplayName}@{room_name}> ` |

## 数据存储

- `_promptState` (`PromptState`) — `CommandHandler` 字段
- `_currentRoomName` (`string?`) — `CommandHandler` 字段，`InRoom` 时非 null
- `RoomCommand.EntryRoomAsync` 增加 `Action<string>? onRoomEntered` 回调参数，成功后从 `response.Get("room_name")` 传回

## 文件改动

### Step 1 — 提示符格式改造

**`Cli/CommandHandler.cs`**
- 新增 `PromptState` 枚举
- 新增 `_promptState` 字段，初始 `Disconnected`
- 改造 `GetPrompt()` 按 `_promptState` 渲染
- `connect` 成功后设置 `_promptState = Connected`
- `OnTcpConnectionLost` 中设置 `_promptState = Disconnected`
- `disconnect` 中设置 `_promptState = Disconnected`

### Step 2 — 房间名称显示

**`Cli/RoomCommand.cs`**
- `ExecuteAsync` 增加 `Action<string>? onRoomEntered` 参数
- `EntryRoomAsync` 成功后读取 `room_name` 调回调

**`Cli/CommandHandler.cs`**
- 新增 `_currentRoomName` 字段
- 调用 `RoomCommand.ExecuteAsync` 时传入回调，设置 `_promptState = InRoom` + `_currentRoomName`
- `GetPrompt` 中 `InRoom` 渲染为 `{DisplayName}@{_currentRoomName}> `
- `OnTcpConnectionLost` / `disconnect` 清理 `_currentRoomName`

## KCP 断开处理

仅 KCP 断开（TCP 仍在）：`KcpConnectionManager.ConnectionLost` 事件中，如果当前 `_promptState == InRoom`，回退到 `Connected` 并清理 `_currentRoomName`。`CommandHandler` 已订阅该事件，在其中补充判断。
