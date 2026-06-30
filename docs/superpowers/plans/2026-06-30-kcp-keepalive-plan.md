# KCP KeepAlive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 KCP 连接添加应用层保活机制，60s 间隔发送 `keepAlive.request` 心跳

**Architecture:** 新增 `KcpKeepAliveService`，完全沿 TCP `KeepAliveService` 结构，发送通道改为 `KcpRpcDispatcher.SendRequestAsync`。Token/sessionId 从 `TcpSessionManager` 读取（与 TCP 一致）。通过 `CommandHandler` 统一管理启动/停止。

**Tech Stack:** .NET (C#), SprotoRpc, Kcp (KumoKyaku)

**设计文档:** `docs/superpowers/specs/2026-06-30-kcp-keepalive-design.md`

## Global Constraints

- 保持与现有 `KeepAliveService` 代码结构一致
- 复用现有 `keepAlive` 协议（tag 3），不新增协议定义
- token/sessionId 从 `TcpSessionManager` 读取，不重复持有

---

### Task 1: 创建 KcpKeepAliveService

**Files:**
- Create: `Connection/KcpKeepAliveService.cs`

**Interfaces:**
- Consumes: `SprotoRpc`（构造 keepAlive 请求）、`TcpSessionManager`（读取 token/sessionId）、`KcpRpcDispatcher`（发送 keepAlive 请求）
- Produces: `KcpKeepAliveService` 类，公开 `Start()` / `Stop()` / `IsRunning` / `Dispose()`

- [ ] **Step 1: 创建 `Connection/KcpKeepAliveService.cs`**

```csharp
using Im.Connection;
using Sproto;

namespace Im.Cli;

/// <summary>
/// KCP keepAlive 定时服务。
/// 每 60 秒通过 <see cref="KcpRpcDispatcher"/> 发送 keepAlive.REQUEST，维持 KCP 会话。
/// 复用现有 keepAlive 协议（tag 3），token/sessionId 从 <see cref="TcpSessionManager"/> 读取。
/// </summary>
/// <remarks>
/// 结构与 <see cref="KeepAliveService"/> 一致，发送通道改为 KcpRpcDispatcher。
/// 发送失败时自动停止循环；KCP 断连时由 CommandHandler 的 OnKcpConnectionLost 兜底停止。
/// </remarks>
public sealed class KcpKeepAliveService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly SprotoRpc _rpc;
    private readonly TcpSessionManager _tcp;
    private readonly KcpRpcDispatcher _kcpDispatcher;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public KcpKeepAliveService(SprotoRpc rpc, TcpSessionManager tcp, KcpRpcDispatcher kcpDispatcher)
    {
        _rpc = rpc;
        _tcp = tcp;
        _kcpDispatcher = kcpDispatcher;
    }

    public void Start()
    {
        lock (_lock)
        {
            StopLocked();
            _cts = new CancellationTokenSource();
            _loopTask = LoopAsync(_cts.Token);
        }
    }

    public void Stop()
    {
        lock (_lock) StopLocked();
    }

    public bool IsRunning
    {
        get { lock (_lock) return _loopTask is not null && !_loopTask.IsCompleted; }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await SendKeepAliveAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    private async Task SendKeepAliveAsync(CancellationToken ct)
    {
        string? token = _tcp.Token;
        long? sessionId = _tcp.SessionId;
        if (string.IsNullOrEmpty(token))
            return;

        try
        {
            // proto: keepAlive.request { session 0 : string; token 1 : string }
            var req = _rpc.C2S.NewSprotoObject("keepAlive.request");
            req["session"] = sessionId?.ToString() ?? "";
            req["token"] = token;

            await _kcpDispatcher.SendRequestAsync("keepAlive", req, ct).ConfigureAwait(false);
            Console.WriteLine("[KCP-KEEPALIVE] 已发送 keepAlive.REQUEST");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 发送失败 — 停止循环，KcpConnectionLost 兜底
            _cts?.Cancel();
        }
    }

    private void StopLocked()
    {
        _cts?.Cancel();

        if (_loopTask is not null)
        {
            try { _loopTask.Wait(TimeSpan.FromSeconds(3)); }
            catch { /* 超时或聚合异常，dispose 兜底 */ }
            try { _loopTask.Dispose(); } catch { }
        }
        _loopTask = null;

        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
```

- [ ] **Step 2: Build 验证**

Run: `dotnet build`
Expected: 编译成功，无错误

- [ ] **Step 3: Commit**

```bash
git add Connection/KcpKeepAliveService.cs
git commit -m "feat(kcp): 新增 KcpKeepAliveService — KCP 连接心跳保活"
```

---

### Task 2: 将 KcpKeepAliveService 集成到 CommandHandler

**Files:**
- Modify: `Cli/CommandHandler.cs`

**Interfaces:**
- Consumes: `KcpKeepAliveService.Start()` / `.Stop()` / `.Dispose()`
- Produces: KCP keepAlive 在以下时机启停：
  - `entry` 命令 create_kcp_session 成功后启动
  - `room entry` / `room create` 的 entry_room 成功后启动
  - `disconnect` / `OnKcpConnectionLost` / `Dispose` 时停止

- [ ] **Step 1: 添加 `_kcpKeepAlive` 字段**

在 `_keepAlive` 字段声明之后添加：

```csharp
private readonly KeepAliveService _keepAlive;
private readonly KcpKeepAliveService _kcpKeepAlive;  // 新增
```

- [ ] **Step 2: 构造函数中创建 KcpKeepAliveService**

在 `_keepAlive = new KeepAliveService(rpc, tcp);` 之后添加：

```csharp
_kcpKeepAlive = new KcpKeepAliveService(rpc, tcp, kcpDispatcher);
```

- [ ] **Step 3: `entry` 命令成功后启动**

在 `entry` case 块内，`create_kcp_session` 成功打印后添加（第 122 行后，`Console.WriteLine($"[INFO] create_kcp_session 响应：ok={ok}");` 之后）：

```csharp
if (ok)
    _kcpKeepAlive.Start();
```

- [ ] **Step 4: `room entry` / `room create` 成功后启动**

修改 `RoomCommand.ExecuteAsync` 调用处的 `onRoomEntered` lambda，在设置 room name 后添加：

```csharp
return await RoomCommand.ExecuteAsync(_rpc, _tcp, _kcp, _kcpDispatcher, input, ct, onRoomEntered: roomName =>
{
    _currentRoomName = roomName;
    _promptState = PromptState.InRoom;
    _kcpKeepAlive.Start();  // 新增
});
```

- [ ] **Step 5: `disconnect` 时停止**

在 `_keepAlive.Stop();` 后添加：

```csharp
_kcpKeepAlive.Stop();
```

- [ ] **Step 6: `OnKcpConnectionLost` 时停止**

在 `OnKcpConnectionLost` 方法开头添加：

```csharp
_kcpKeepAlive.Stop();
```

- [ ] **Step 7: `Dispose` 时释放**

在 `_keepAlive.Dispose();` 后添加：

```csharp
_kcpKeepAlive.Dispose();
```

- [ ] **Step 8: Build 验证**

Run: `dotnet build`
Expected: 编译成功，无错误

- [ ] **Step 9: Commit**

```bash
git add Cli/CommandHandler.cs
git commit -m "feat(kcp): CommandHandler 集成 KcpKeepAliveService 启停"
```

---

### Task 3: 构建并快速验证

- [ ] **Step 1: 完整 build**

```bash
dotnet build
```

Expected: `Build succeeded.` 0 warnings, 0 errors

- [ ] **Step 2: 代码审查——确认改动完整性**

检查点：
- `KcpKeepAliveService` 日志前缀为 `[KCP-KEEPALIVE]` 以区分于 TCP 的 `[KEEPALIVE]`
- `entry` 命令失败分支不启动 keepAlive（仅在 `ok=true` 时）
- `room entry/create` 仅在 `onRoomEntered` 回调中启动（即 entry_room 成功后）
- `disconnect` 同时停止 TCP 和 KCP keepAlive
- `OnKcpConnectionLost` 停止 KCP keepAlive
- `Dispose` 释放 KCP keepAlive

- [ ] **Step 3: Commit（若无未提交改动则跳过）**

```bash
git add -A
git commit -m "chore: build 验证通过"
```
