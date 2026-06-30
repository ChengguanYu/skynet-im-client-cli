---
name: kcp-keepalive-design
description: KCP 连接 keepAlive 设计 — 复用 TCP keepAlive 协议（tag 3）
---

# KCP KeepAlive 设计

## 目标

给 KCP 连接添加应用层保活机制，持续向服务器发送心跳，使空闲连接也能被检测到断开（与 TCP keepAlive 行为一致）。

## 现状

- TCP 有 `KeepAliveService`：60s `PeriodicTimer`，通过 `TcpSessionManager` 发送 `keepAlive.request { session, token }`
- KCP 只有被动断线检测：仅在 `WaitSnd > 0`（有积压数据）时监测 10s 进展，**空闲连接永远不会检测到断线**

## 方案：KcpKeepAliveService

完全沿用 TCP `KeepAliveService` 的结构，发送通道改为 `KcpRpcDispatcher.SendRequestAsync`。

### 新类

**文件：** `Cli/KcpKeepAliveService.cs`

| 维度 | 实现 |
|---|---|
| 定时器 | `PeriodicTimer` 60s（与 TCP 一致）|
| 协议 | `keepAlive.request { session, token }`（复用 tag 3）|
| token/sessionId | 从 `TcpSessionManager` 读取（与 TCP 一致）|
| 发送通道 | `KcpRpcDispatcher.SendRequestAsync("keepAlive", req, ct)` |
| 报错处理 | 发送异常 → cancel 自身 `_cts` → 停止循环 |

### 集成点

**修改 `Cli/CommandHandler.cs`：**

- 构造函数创建 `KcpKeepAliveService`
- 启动时机：
  - `entry` 命令的 `create_kcp_session` 成功之后
  - `room entry` / `room create` 的 `entry_room` 成功之后
- 停止时机：
  - `disconnect` 命令
  - `OnKcpConnectionLost` 回调（双重保障：KCP 本身断线 + keepAlive 发送失败都会触发）
  - `dispose` / `quit`

**无需修改 `Program.cs`：**`KcpKeepAliveService` 在 `CommandHandler` 构造函数内创建（与 TCP `KeepAliveService` 一致）。

### 异常处理

KCP keepAlive 发送失败时，两个路径都会触发停止：

1. **send 异常** → `KcpKeepAliveService.SendKeepAliveAsync` catch → cancel `_cts`
2. **KCP 通道实际断开** → `OnKcpConnectionLost` → 停止 keepAlive

两者独立工作，任一先到即停止。

## 文件改动清单

| 文件 | 改动 |
|---|---|
| `Cli/KcpKeepAliveService.cs` | 新增 |
| `Cli/CommandHandler.cs` | 创建/启动/停止 KcpKeepAliveService |
| `Program.cs` | 注入 KcpKeepAliveService |
