# Schemata 紧急修复与协议面实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按 `2026-10-17-urgent-remediation-spec.md` 完成 P0 安全/持久化修复、Tenancy 边界收敛、全组件自定义实体、SCIM 最小合规、RFC 8288 Web Linking 和一致性清理。

**Architecture:** 保持一个 dispatcher、一个 repository mutation/UoW owner、一个 scheduling settlement owner、一个 cache backend capability、一个 SCIM protocol owner、一个 Link serializer。默认入口只把内置实体传给泛型实现；可选能力由 DI/keyed/enumerable advisor 选择。每个工作流可独立发布，Wave 内按依赖顺序执行。

**Tech Stack:** .NET 8/10、ASP.NET Core DI、xUnit v2 + Moq、EF Core/LinqToDB、Memory/Redis cache、RabbitMQ transport、AutoMapper/Mapster、现有 source generators。

**Spec:** `docs/superpowers/specs/2026-10-17-urgent-remediation-spec.md`

## Global Constraints

- 每个技术需求只有一个 owner。dispatcher、repository mutation/UoW、cache idempotency、scheduling execution store、SCIM protocol、Link serializer 各保留一个实现。
- 可选能力通过 `GetService`/`GetRequiredService`/`GetServices`、keyed 或 enumerable advisor 选择。运行时 bool 不承担能力分路。
- advisor 与 context 不携带状态标志；业务数据通过显式 payload 或 DI 服务传递。禁止静态类或静态值承载每请求状态。
- 租户逻辑只存在于 `Schemata.Tenancy.*`。其他 component 不读取 `TenantContext`、不比较 `TenantUid`、不拼租户缓存键、不解析租户消息键、不选择 tenant scope。
- 需要 per-tenant 的 Schemata 服务由用户在 tenant 注册中提供；除 Tenancy 自身外，component 不探测 tenant、不创建 tenant scope、不为 tenant 自动启动后台服务。
- 框架默认实体不实现 `ITenant`。需要 shared-table 隔离的用户继承内置实体、实现 `ITenant`，并在启用 feature 时传入自定义类型。
- Repository 隔离有两种模式：分库实例；shared table + `ITenant` 字段。Scheduling 只使用 repository 数据隔离，不提供 adapter 隔离。
- 系统自动环节不生成或注入 principal；调用者入口继续执行既有 authentication/authorization。
- `[ResourceReference]` 保持 string canonical-name 关联，支持单一类型与按 canonical name 解析的多态目标。
- 纯字符串常量字典归 `SchemataConstants` 或对应 component 的 `Constants` 类型。
- 测试观察最终业务结果、持久化行、拒绝路径、竞态胜负、scope disposal。descriptor count、cache key 字符串、mock echo 不作为唯一断言。
- 不新增兼容 alias、双写、旧数据读取、startup guard、遥测或迁移工具。不推送、不合并、不关闭 issue，除非用户另行授权。
- 泛型实体参数直接使用实体名（`TActor`、`TJob`、`TSession`）；不给 entity 参数加 `Entity` 后缀。名称冲突时给 service 类型参数加后缀（`TSession` 与 `TSessionManager`）。

## Review Focus

1. Token store 的 Add/Update 返回 `NoWrite` 或 advisor veto：不得提交空事务、不得返回成功、不得留下部分 successor。
2. Scheduling 结算中 execution 已 terminal 但 job state/timer 未落盘：不得丢失 NextRunTime，不得让 one-time job 残留 Active。
3. 用户用自定义 `ITenant` 实体启用 shared-table：host 不得看见 tenant 行，跨租户 update/remove 必须明确失败，分库模式不得要求 `ITenant`。
4. SCIM 请求声明未实现的 optional 能力或 malformed 属性：不得在 `ServiceProviderConfig` 虚报支持，不得返回 AIP-193 错误体。
5. Link header 的 quoted-string、多重 link、相对 target、anchor 和恶意输入：parser/serializer 必须 round-trip，不得泄漏 secret/tenant id。

---

## 文件结构

| Wave | Owner | 主要文件/新包 |
|---|---|---|
| A | Security/Tenancy/Scheduling/Authorization | `RepositoryTokenStore`、`TenantCookieRegistration`、`JobExecutionDispatcher`、`MemoryCacheTenantProviderCache`、`SessionCache`、`ScopedAuthenticationService`、`AuthorizationDetailsService` |
| G | 各实体 component | `Use*` 入口、builder、feature、handler、repository、mapping、HTTP feature、`ActorStateStore`、`Tenancy.Actor` |
| B | Tenancy + Entity.Repository + Scheduling | `SchemataTenancyBuilder`、`TenantRepositoryBuilder`、Tenancy 实体、`IRepository` suppression、Tenancy 文档 |
| C | Core/Resource/Identity/Scheduling/Advice/Flow/Modeling | `ResourceRegistry`、`IHandledResponse<T>`、`AdviceRunner` generator、`RegistrationMetadataMapper`、`CallActivityExecutor`、`Parser` |
| D | 新包 `Schemata.Scim`、`Schemata.Identity.Scim` | 共享 SCIM 协议、discovery、Users/Groups adapter、HTTP 集成测试 |
| E | `Schemata.Transport.Http` + 各 HTTP component | `WebLink`/`LinkHeader`/`ILinkContributor`、result filter、各组件 contributor |
| F | 全仓 | 残留目录、包计数、`ISimpleMapper`、`AdviceContext` ambient/bag |

---

## Wave A：P0 安全与持久化

### Task 1: Token store 强制 mutation Applied

**Issues:** #335  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Security.Foundation/Stores/RepositoryTokenStore.cs`
- Test: `tests/Schemata.Security.Tests/RepositoryTokenStoreShould.cs`
- Test: `tests/Schemata.Security.Tests/RepositoryTokenStoreIntegrationShould.cs`
- Test: `tests/Schemata.Authorization.Integration.Tests/TokenLifecycleShould.cs`

**Interfaces:**
- Consumes: `IRepository<Token>.AddAsync/UpdateAsync` returns `MutationResult`; `SchemataResourceErrors.PreconditionFailed<Token>()`; `SchemataResourceErrors.Aborted<Token>()`.
- Produces: every mandatory write path either returns `MutationResult.Applied` or throws before `CommitAsync`; no success path after a non-applied write.

- [ ] **Step 1: 标记必需写入**
  在 `CreateAsync`、`UpdateAsync`、`SetAsync`、`GetOrCreateCoreAsync` 中逐项标记 publication、rotation predecessor/successor/marker、family invalidation、revocation 为必需写入。prune 和真实幂等 no-op 保持现有语义。
- [ ] **Step 2: 补齐 `SetAsync` 和 `GetOrCreateCoreAsync`**
  `SetAsync` 的每个 `AddAsync`/`UpdateAsync` 检查 `MutationResult.Applied`；未应用时抛 `SchemataResourceErrors.PreconditionFailed<SchemataToken>()`。`GetOrCreateCoreAsync` 在 lost `TryAdd` 后仍读不到 winner 时抛 `AbortedException`，不返回 `null`、不重试写入。
- [ ] **Step 3: 统一必需 helper 的失败类型**
  将 `AddMandatoryAsync`/`UpdateMandatoryAsync` 的 veto 结果从 `AbortedException` 改为 spec 指定的 `PreconditionFailed<SchemataToken>()`；保留非必需路径的现有结果。未提交 UoW dispose 回滚已 staged writes。
- [ ] **Step 4: 写失败测试**
  在 `RepositoryTokenStoreShould` 增加 `Create_WhenAddIsVetoed_DoesNotCommit`、`Update_WhenUpdateIsVetoed_DoesNotCommit`、`Set_WhenMandatoryWriteIsVetoed_ThrowsPreconditionFailed`、`GetOrCreate_WhenWinnerDisappears_ThrowsAborted`。在 integration fixture 中增加 add veto、update veto、commit failure、lost TryAdd + empty re-read，断言存储行和返回失败。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Security.Tests/Schemata.Security.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Security.Tests/Schemata.Security.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RepositoryTokenStore"`
  Expected: PASS。失败只重跑失败用例，不重复成功命令。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Security.Foundation/Stores/RepositoryTokenStore.cs tests/Schemata.Security.Tests/RepositoryTokenStoreShould.cs tests/Schemata.Security.Tests/RepositoryTokenStoreIntegrationShould.cs`
  `git commit -m "fix(Security): require applied token mutations"`

### Task 2: 删除 cookie probe.log 写入

**Issues:** #334  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Tenancy.Foundation/State/TenantCookieRegistration.cs:47-65`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantCookieShould.cs`

**Interfaces:**
- Consumes: `IConfigureOptions<CookieAuthenticationOptions>`; existing `ILogger` registration.
- Produces: cookie options construction performs no file I/O in `ApplicationOptions.WorkingDirectory`.

- [ ] **Step 1: 删除写入**
  删除 `TenantCookieRegistration.cs:59` 的 `File.AppendAllText("probe.log", ...)`。不新增静态日志文件、不在 options 构建中写工作目录。
- [ ] **Step 2: 写只读内容根测试**
  在 `TenantCookieShould` 增加 `Configure_WithReadOnlyContentRoot_WritesNoFile`：创建只读临时目录，构建 cookie options，断言目录文件集合不变且不存在 `probe.log`。
- [ ] **Step 3: 单次验证**
  Run: `dotnet build tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantCookieShould"`
  Expected: PASS。
- [ ] **Step 4: Commit**
  `git add src/Schemata.Tenancy.Foundation/State/TenantCookieRegistration.cs tests/Schemata.Tenancy.Integration.Tests/TenantCookieShould.cs`
  `git commit -m "fix(Tenancy): stop writing probe log during cookie setup"`

### Task 3: Scheduling 终态与 job 状态单边界结算

**Issues:** #345  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Scheduling.Foundation/JobExecutionDispatcher.cs:283-352`
- Create: `src/Schemata.Scheduling.Foundation/Settlement/IJobSettlement.cs`
- Create: `src/Schemata.Scheduling.Foundation/Settlement/JobSettlement.cs`
- Modify: `src/Schemata.Scheduling.Foundation/Handlers/DefaultStageJobExecutionResultHandler.cs`
- Modify: `src/Schemata.Scheduling.Foundation/Handlers/DefaultScheduleJobHandler.cs`
- Modify: `src/Schemata.Scheduling.Foundation/Commands/StageJobExecutionResultRequest.cs`
- Modify: `src/Schemata.Scheduling.Foundation/Extensions/SchemataSchedulingServiceCollectionExtensions.cs:63-67`
- Test: `tests/Schemata.Scheduling.Tests/JobExecutionDispatcherShould.cs`
- Test: `tests/Schemata.Scheduling.Tests/StageJobExecutionResultHandlerShould.cs`
- Test: `tests/Schemata.Scheduling.Integration.Tests/ScheduleVetoShould.cs`
- Test: `tests/Schemata.Scheduling.Integration.Tests/SchemataJobWriteGateShould.cs`

**Interfaces:**
```csharp
internal interface IJobSettlement
{
    Task<SettlementOutcome> SettleAsync(
        JobExecutionLease lease,
        JobExecutionResult result,
        CancellationToken ct = default);
}

internal readonly record struct SettlementOutcome(bool ExecutionSettled, bool ScheduleSettled, Exception? Failure);
```
- Consumes: `IRepository<SchemataJob>`, `IJobExecutionStore`, existing UoW/mutation APIs, `MissedFirePolicy`.
- Produces: one scheduling-owned settlement entry; no public `StageJobExecutionResultRequest` handler; one missed-fire calculator used by both scheduling handlers.

- [ ] **Step 1: 建立单一结算 owner**
  新建 `IJobSettlement`/`JobSettlement`。在同一 UoW 内完成 execution settle 与 job `State`/`NextRunTime`/`RecentRunTime`/`RecentError` 更新；provider 不支持跨 store 事务时，先持久化可重试的 pending stage，再 settle execution。`JobExecutionDispatcher.FinalizeAsync` 只调用 `IJobSettlement`，不再 `SendAsync<StageJobExecutionResultRequest, Unit>`。
- [ ] **Step 2: 隐藏 staging request**
  删除 `StageJobExecutionResultRequest` 的公共 handler 注册；把 `DefaultStageJobExecutionResultHandler` 的逻辑移入 `JobSettlement` 并从 DI 移除该 request。若 record 仍需内部使用，改为 `internal sealed record`，不暴露 consumer advisor 入口。
- [ ] **Step 3: 收敛 missed-fire 计算**
  把 `DefaultScheduleJobHandler.AdjustForMissedWindow` 与 `DefaultStageJobExecutionResultHandler` 的重复计算移到 `JobSettlement` 使用的单一 `MissedFireWindow` helper；`DefaultScheduleJobHandler` 只调用该 helper。
- [ ] **Step 4: 写结算测试**
  `JobExecutionDispatcherShould` 覆盖 staging 永久失败、advisor veto、正常成功；`ScheduleVetoShould`/`SchemataJobWriteGateShould` 断言 execution 终态与 job schedule 不丢失；one-time 不残留 Active/旧 NextRunTime；recurring 继续 re-arm。`StageJobExecutionResultHandlerShould` 改为断言 `IJobSettlement` 行为，不再断言公共 handler 注册。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Scheduling.Tests/Schemata.Scheduling.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Scheduling.Tests/Schemata.Scheduling.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~JobExecutionDispatcherShould|FullyQualifiedName~StageJobExecutionResultHandlerShould"`
  Run: `dotnet test tests/Schemata.Scheduling.Integration.Tests/Schemata.Scheduling.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ScheduleVetoShould|FullyQualifiedName~SchemataJobWriteGateShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Scheduling.Foundation tests/Schemata.Scheduling.Tests tests/Schemata.Scheduling.Integration.Tests`
  `git commit -m "fix(Scheduling): settle execution and schedule in one owner"`

### Task 4: Provider cache 去除 ThreadStatic 与同步等待

**Issues:** #337  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Tenancy.Foundation/Services/MemoryCacheTenantProviderCache.cs:16-392`
- Modify: `src/Schemata.Tenancy.Skeleton/ITenantProviderCache.cs`
- Test: `tests/Schemata.Tenancy.Tests/MemoryCacheTenantProviderCacheShould.cs`
- Test: `tests/Schemata.Tenancy.Tests/MemoryCacheTenantProviderCacheLeaseShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantProviderLeaseShould.cs`

**Interfaces:**
- Consumes: `ITenantProviderCache`, `ITenantProviderLease`, `Pending`.
- Produces:
```csharp
ValueTask<ITenantProviderLease> LeaseVersionAsync(
    string id,
    Func<IServiceProvider> create,
    CancellationToken ct = default);
```
所有等待者共享 per-key `Pending.Completion`，取消通过 `ct` 传播。

- [ ] **Step 1: 替换 ThreadStatic 守卫**
  删除 `[ThreadStatic] _constructing`、`EnterConstruction`、`ExitConstruction`、`IsConstructing`。构建所有权记录在 per-key `Pending`；同一 key 的并发调用等待同一个 completion，不依赖线程身份。
- [ ] **Step 2: 异步等待与释放**
  `LeaseVersionAsync` 使用可取消的 `await pending.Completion.Task.WaitAsync(ct)`，删除 `pending.Wait()`。`Release`/`DisposeIfZero`/`DisposeProviders` 改为 `await DisposeProviderAsync(provider)`；删除 `DisposeProvider` 与 `GetAwaiter().GetResult()`。
- [ ] **Step 3: 更新调用点**
  修改 `ITenantProviderCache` 及所有调用者为异步；不保留同步/异步两份功能相同的 lease 路径。Provider 构建异常、取消、退休释放都走同一 completion 完成/清理路径。
- [ ] **Step 4: 写并发测试**
  `MemoryCacheTenantProviderCacheShould` 增加同一 tenant 首请求并发、等待期间取消、构建异常、provider 退休释放；断言没有线程池阻塞、没有 provider 泄漏、只有一个构建者。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Tenancy.Tests/Schemata.Tenancy.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Tenancy.Tests/Schemata.Tenancy.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MemoryCacheTenantProviderCache"`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantProviderLeaseShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Tenancy.Foundation/Services/MemoryCacheTenantProviderCache.cs src/Schemata.Tenancy.Skeleton/ITenantProviderCache.cs tests/Schemata.Tenancy.Tests tests/Schemata.Tenancy.Integration.Tests/TenantProviderLeaseShould.cs`
  `git commit -m "fix(Tenancy): await provider cache leases without blocking"`

### Task 5: SessionCache 只在同步 backend 上提供同步成员

**Issues:** #342  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Tenancy.Caching/SessionCache.cs:20-70`
- Create: `src/Schemata.Caching.Skeleton/ISynchronousCacheBackend.cs`
- Modify: `src/Schemata.Caching.Memory/MemoryCacheBackend.cs`
- Modify: `src/Schemata.Caching.Redis/RedisCacheBackend.cs`
- Modify: `src/Schemata.Tenancy.Caching/TenantCacheBuilder.cs`
- Modify: `src/Schemata.Tenancy.Foundation/State/TenantSessionRegistration.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantSessionShould.cs`
- Test: `tests/Schemata.Caching.Memory.Integration.Tests/MemoryCacheProviderShould.cs`
- Test: `tests/Schemata.Caching.Redis.Tests/RedisCacheProviderShould.cs`

**Interfaces:**
```csharp
public interface ISynchronousCacheBackend
{
    byte[]? Get(string key);
    void Set(string key, byte[] value, DistributedCacheEntryOptions options);
    void Refresh(string key);
    void Remove(string key);
}
```
`MemoryCacheBackend` implements it; Redis does not. `SessionCache` resolves the optional capability from the tenant scope.

- [ ] **Step 1: 增加显式同步能力**
  新建 `ISynchronousCacheBackend`，只由有界同步 backend 实现。Redis backend 不实现该接口；不添加 `SupportsSync` bool。
- [ ] **Step 2: 改造 SessionCache**
  `SessionCache` 构造函数接收 `ICacheBackend` 与 `IServiceProvider`，通过 `GetService<ISynchronousCacheBackend>()` 获取同步能力。同步成员在能力不存在时抛 `NotSupportedException`；异步成员直接等待 provider。请求路径只调用异步成员。
- [ ] **Step 3: 注册能力**
  在 tenant cache 构建中把选中的同步 backend 注册为 `ISynchronousCacheBackend`；不自动包装 host `ICacheProvider`，只在显式 `UseCache` 的 tenant 注册。
- [ ] **Step 4: 写后端测试**
  `TenantSessionShould` 覆盖慢 Redis 后端下请求线程不被阻塞、异步取消传播、session 读写与 tenant namespace 一致；Memory 测试覆盖同步成员可用；Redis 测试覆盖同步成员明确拒绝且异步路径正常。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantSessionShould"`
  Run: `dotnet test tests/Schemata.Caching.Redis.Tests/Schemata.Caching.Redis.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RedisCacheProviderShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Caching.Skeleton/ISynchronousCacheBackend.cs src/Schemata.Caching.Memory src/Schemata.Caching.Redis src/Schemata.Tenancy.Caching src/Schemata.Tenancy.Foundation/State/TenantSessionRegistration.cs tests/Schemata.Tenancy.Integration.Tests/TenantSessionShould.cs tests/Schemata.Caching.Memory.Integration.Tests tests/Schemata.Caching.Redis.Tests`
  `git commit -m "fix(Tenancy): stop sync-over-async in session cache"`

### Task 6: Authentication scope 显式路由

**Issues:** #348  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Core/State/ScopedAuthenticationService.cs:20-120`
- Modify: `src/Schemata.Core/State/AuthenticationSchemeScope.cs:13`
- Modify: `src/Schemata.Tenancy.Foundation/State/TenantAuthenticationScope.cs:51-55`
- Modify: `src/Schemata.Tenancy.Foundation/State/TenantStateBuilder.cs:31`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantSessionShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantAntiforgeryShould.cs`
- Create: `tests/Schemata.Core.Tests/ScopedAuthenticationServiceShould.cs`

**Interfaces:**
- Consumes: keyed/optional `IAuthenticationService`; `HttpContext.RequestServices` read-only.
- Produces: `AuthenticationSchemeScope` carries the target `IServiceProvider` (or keyed service key), not an opaque `object ScopeKey`; `ScopedAuthenticationService` never assigns `context.RequestServices`.

- [ ] **Step 1: 删除 RequestServices 改写**
  `AuthenticateAsync`/`ChallengeAsync`/`ForbidAsync`/`SignInAsync`/`SignOutAsync`/`BindAsync` 不再保存、替换、恢复 `context.RequestServices`。改为从显式 scope provider 解析 keyed/optional `IAuthenticationService` 并调用。
- [ ] **Step 2: 收敛 scope 数据**
  删除 `AuthenticationSchemeScope.ScopeKey`，改为 `IServiceProvider Scope`（或 keyed service key）；`TenantAuthenticationScope`/`TenantStateBuilder` 同步更新。删除未使用的 `InnerServiceKey`。
- [ ] **Step 3: 写连续/并发认证测试**
  `ScopedAuthenticationServiceShould` 覆盖 tenant-owned scheme 与 host scheme 在同一请求内连续认证、并发认证不观察错误 provider、challenge/sign-in/sign-out 语义保持；测试断言原 `RequestServices` 引用始终不变。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Core.Tests/Schemata.Core.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Core.Tests/Schemata.Core.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ScopedAuthenticationServiceShould"`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantSessionShould|FullyQualifiedName~TenantAntiforgeryShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Core/State src/Schemata.Tenancy.Foundation/State tests/Schemata.Core.Tests/ScopedAuthenticationServiceShould.cs tests/Schemata.Tenancy.Integration.Tests`
  `git commit -m "fix(Core): route authentication through explicit scopes"`

### Task 7: AuthorizationDetailsService JsonDocument 释放

**Issues:** #296  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Services/AuthorizationDetailsService.cs:48-71`
- Test: `tests/Schemata.Authorization.Tests/AuthorizationDetailsServiceShould.cs`
- Test: `tests/Schemata.Authorization.Tests/AuthorizationDetailsNarrowingShould.cs`

**Interfaces:**
- Consumes: `JsonDocument.Parse`, `JsonElement.Clone`.
- Produces: `Parse` disposes the document on success and every validation failure; returned `JsonArray` remains independent.

- [ ] **Step 1: 使用 using**
  将 `JsonDocument document; try { document = ... }` 改为 `using var document = JsonDocument.Parse(raw);`。校验失败分支保持异常类型与消息；返回前 `element.Clone()`。
- [ ] **Step 2: 写行为测试**
  `AuthorizationDetailsServiceShould` 增加 `Parse_WhenInvalid_ThrowsWithoutLeakingDocument`（重复异常路径）和 `Parse_ReturnsClonedArrayUsableAfterReturn`；`AuthorizationDetailsNarrowingShould` 断言返回数组不依赖 document 生命周期。
- [ ] **Step 3: 单次验证**
  Run: `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AuthorizationDetails"`
  Expected: PASS。
- [ ] **Step 4: Commit**
  `git add src/Schemata.Authorization.Foundation/Services/AuthorizationDetailsService.cs tests/Schemata.Authorization.Tests`
  `git commit -m "fix(Authorization): dispose parsed authorization details"`

---

## Wave G：通用实体替换

### Task 8: Actor 与 Tenancy.Actor 泛型实体

**Issues:** 通用实体替换 G2  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Actor.Foundation/Extensions/SchemataBuilderExtensions.cs:23`
- Modify: `src/Schemata.Actor.Foundation/SchemataActorBuilder.cs:47`
- Modify: `src/Schemata.Actor.Foundation/Runtime/ActorStateStore.cs:14`
- Modify: `src/Schemata.Actor.Foundation/Runtime/ActorInstance.cs:435`
- Modify: `src/Schemata.Actor.Foundation/Runtime/InProcessActorSystem.cs`
- Modify: `src/Schemata.Actor.Foundation/Schemata.Actor.Foundation.csproj:20-21`
- Modify: `src/Schemata.Actor.Scheduling/Runtime/ActorReminders.cs:43-73`
- Modify: `src/Schemata.Tenancy.Actor/TenantActorExtensions.cs:14-26`
- Modify: `src/Schemata.Tenancy.Actor/TenantActorSystem.cs:26-29`
- Test: `tests/Schemata.Actor.Foundation.Tests/`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantActorShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantActorLifetimeShould.cs`

**Interfaces:**
```csharp
public static SchemataActorBuilder UseActor<TActor>(
    this SchemataBuilder builder,
    Action<SchemataActorBuilder>? configure = null)
    where TActor : SchemataActor, new();

public sealed class ActorStateStore<TActor>(IRepository<TActor> repository)
    where TActor : SchemataActor, new();

public static SchemataTenancyBuilder<TTenant> UseActor<TTenant, TActor>(
    this SchemataTenancyBuilder<TTenant> builder)
    where TTenant : SchemataTenant
    where TActor : SchemataActor, new();
```
- Converts `ActorStateStore`/`ActorInstance`/`InProcessActorSystem`/`TenantActorSystem` to closed `TActor`.
- Default non-generic `UseActor()` forwards to `UseActor<SchemataActor>()`; no second implementation.

- [ ] **Step 1: 泛型入口与 builder**
  添加 `UseActor<TActor>`；`SchemataActorBuilder` 的实体相关注册闭包到 `TActor`。默认入口只调用泛型实现。
- [ ] **Step 2: 关闭运行时**
  `ActorStateStore<TActor>`、`ActorInstance<TActor>`、`InProcessActorSystem<TActor>` 使用 `IRepository<TActor>`；`ActorReminders` 接受同一闭合类型。删除 `Actor.Foundation` 的 `InternalsVisibleTo("Schemata.Tenancy.Actor")`。
- [ ] **Step 3: Tenancy.Actor 公开 seam**
  `TenantActorSystem<TActor>` 通过 Actor.Foundation 公开的 factory/runtime seam 构造；`Tenancy.Actor.UseActor<TTenant,TActor>()` 和 `UseActorPersistence<TTenant,TActor>()` 注册 `ActorStateStore<TActor>`；不替换不属于自己的 descriptor。
- [ ] **Step 4: 自定义 Actor 测试**
  新增 `SessionActor : SchemataActor`，通过 `UseActor<SessionActor>()` 注册 repository/EF/LinqToDB 映射并完成一次 actor turn/state save；`TenantActorShould` 使用自定义 `TActor` 完成 tenant 持久化。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Actor.Foundation.Tests/Schemata.Actor.Foundation.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Actor.Foundation.Tests/Schemata.Actor.Foundation.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ActorStateStore|FullyQualifiedName~ActorInstance"`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantActorShould|FullyQualifiedName~TenantActorLifetimeShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Actor.Foundation src/Schemata.Actor.Scheduling src/Schemata.Tenancy.Actor tests/Schemata.Actor.Foundation.Tests tests/Schemata.Tenancy.Integration.Tests/TenantActorShould.cs tests/Schemata.Tenancy.Integration.Tests/TenantActorLifetimeShould.cs`
  `git commit -m "feat(Actor)!: close actor entities through UseActor"`

### Task 9: Scheduling 泛型 Job/Execution

**Issues:** 通用实体替换 G2  
**Dependencies:** Task 8（Actor.Scheduling bridge 使用同一闭合类型）  
**Files:**
- Modify: `src/Schemata.Scheduling.Foundation/Extensions/SchemataBuilderExtensions.cs:12`
- Modify: `src/Schemata.Scheduling.Foundation/SchedulingBuilder.cs`
- Modify: `src/Schemata.Scheduling.Foundation/Features/SchemataSchedulingFeature.cs`
- Modify: `src/Schemata.Scheduling.Foundation/JobExecutionDispatcher.cs:55-57,171,313-321`
- Modify: `src/Schemata.Scheduling.Foundation/DefaultOperationService.cs:44,128,171,188`
- Modify: `src/Schemata.Scheduling.Foundation/SchedulingHandlerSupport.cs:44`
- Modify: `src/Schemata.Scheduling.Foundation/DefaultTriggerJobHandler.cs:27`
- Modify: `src/Schemata.Scheduling.Foundation/SchedulingInitializer.cs:74`
- Modify: `src/Schemata.Scheduling.Foundation/RunJobHandler.cs`
- Modify: `src/Schemata.Scheduling.Foundation/SchedulingResourceRegistration.cs`
- Modify: `src/Schemata.Scheduling.Skeleton/Services/IJobExecutionStore.cs`
- Test: `tests/Schemata.Scheduling.Tests/`
- Test: `tests/Schemata.Scheduling.Integration.Tests/`
- Test bridges: `tests/Schemata.Actor.Scheduling.Tests/`, `tests/Schemata.Flow.Integration.Tests/`, `tests/Schemata.Push.Actor.Tests/`, `tests/Schemata.Report.Actor.Tests/`

**Interfaces:**
```csharp
public static SchedulingBuilder<TJob, TExecution> UseScheduling<TJob, TExecution>(
    this SchemataBuilder builder)
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new();

public interface IJobExecutionStore<TJob, TExecution>
    where TJob : SchemataJob
    where TExecution : SchemataJobExecution
```
- Default `UseScheduling()` forwards to `UseScheduling<SchemataJob, SchemataJobExecution>()`.
- Bridge features accept upstream `TJob,TExecution`; no second job/execution entity.

- [ ] **Step 1: 泛型 builder/feature**
  添加 `SchedulingBuilder<TJob,TExecution>` 和泛型 `UseScheduling`；feature 注册 `IRepository<TJob>`/`IRepository<TExecution>`、resource registration、store、dispatcher。
- [ ] **Step 2: 关闭内部实现**
  `JobExecutionDispatcher`、`DefaultOperationService`、`SchedulingHandlerSupport`、`DefaultTriggerJobHandler`、`SchedulingInitializer`、`RunJobHandler` 全部使用 `TJob`/`TExecution`，删除 `new SchemataJob`/`new SchemataJobExecution` 和硬编码 `IRepository<SchemataJob*>`。
- [ ] **Step 3: 传播到 bridge**
  通过 LSP references 更新 `Actor.Scheduling`、`Flow.Scheduling`、`Push.Actor`、`Report.Actor` 等调用 `UseScheduling` 的 bridge；bridge 接受上游 `TJob,TExecution` 参数。
- [ ] **Step 4: 自定义 Job 测试**
  新增 `MyJob : SchemataJob`、`MyExecution : SchemataJobExecution`，通过 `UseScheduling<MyJob,MyExecution>()` 完成 EF/LinqToDB 映射、trigger、run、execution query；默认类型测试保持。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Scheduling.Tests/Schemata.Scheduling.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Scheduling.Tests/Schemata.Scheduling.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Scheduling"`
  Run: `dotnet test tests/Schemata.Scheduling.Integration.Tests/Schemata.Scheduling.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LinqToDbExecutionShould|FullyQualifiedName~ExecutionStoreContractShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Scheduling.Foundation src/Schemata.Scheduling.Skeleton tests/Schemata.Scheduling.Tests tests/Schemata.Scheduling.Integration.Tests`
  `git commit -m "feat(Scheduling)!: close job entities through UseScheduling"`

### Task 10: Flow 泛型实体

**Issues:** 通用实体替换 G2  
**Dependencies:** Task 9（Flow.Scheduling 使用 `TJob,TExecution`）  
**Files:**
- Modify: `src/Schemata.Flow.Foundation/Extensions/FlowBuilderExtensions.cs:21`
- Modify: `src/Schemata.Flow.Foundation/SchemataFlowBuilder.cs`
- Modify: `src/Schemata.Flow.Foundation/Features/SchemataFlowFeature.cs`
- Modify: `src/Schemata.Flow.Bpmn/BpmnEngine.cs:1184`
- Modify: `src/Schemata.Flow.Bpmn/Runtime/SubProcesses/CallActivityExecutor.cs:70`
- Modify: `src/Schemata.Flow.Repository/Extensions/SchemataFlowRepositoryServiceCollectionExtensions.cs:14`
- Modify: `src/Schemata.Flow.Repository/SchemataFlowEffectIntent.cs`
- Modify: `src/Schemata.Flow.Scheduling/Handlers/FlowTimerCatchHandler.cs:127`
- Modify: `src/Schemata.Flow.Http/Features/SchemataFlowHttpFeature.cs`
- Test: `tests/Schemata.Flow.Integration.Tests/FlowTenantRepositoryShould.cs`
- Test: `tests/Schemata.Flow.Integration.Tests/`
- Test: `tests/Schemata.Flow.Bpmn.Tests/`

**Interfaces:**
```csharp
public static SchemataFlowBuilder UseFlow<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
    this SchemataBuilder builder)
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
    where TEffectIntent : SchemataFlowEffectIntent, new();
```
- Default `UseFlow()` forwards to the seven built-in entities.
- `Flow.Http` and repository registration close on the same seven types.

- [ ] **Step 1: 泛型 builder/feature**
  添加 `SchemataFlowBuilder<...>` 和泛型 `UseFlow`；feature/repository/HTTP registration 使用闭合类型。默认入口只调用泛型实现。
- [ ] **Step 2: 迁移内部构造**
  `BpmnEngine`、`CallActivityExecutor`、`FlowTimerCatchHandler`、Flow handlers/repository 删除 `new SchemataProcess*` 与硬编码 repository 类型，改用 `TProcess/TToken/...`。Flow.Scheduling 的 `new SchemataJob` 改为上游 `TJob`。
- [ ] **Step 3: 迁移测试与自定义类型**
  `FlowTenantRepositoryShould` 改用自定义 Flow 子类型（实现 `ITenant`），不直接构造默认 `SchemataProcess*`；增加一个完整 `UseFlow<...>` 端到端测试，完成 start/signal/complete 与 EF/LinqToDB 映射。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Flow.Integration.Tests/Schemata.Flow.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Flow.Integration.Tests/Schemata.Flow.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~FlowTenantRepositoryShould|FullyQualifiedName~CallActivityChildRuntimeShould"`
  Run: `dotnet test tests/Schemata.Flow.Bpmn.Tests/Schemata.Flow.Bpmn.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CallActivity"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Flow.Foundation src/Schemata.Flow.Bpmn src/Schemata.Flow.Repository src/Schemata.Flow.Scheduling src/Schemata.Flow.Http tests/Schemata.Flow.Integration.Tests tests/Schemata.Flow.Bpmn.Tests`
  `git commit -m "feat(Flow)!: close flow entities through UseFlow"`

### Task 11: Security Token 泛型

**Issues:** 通用实体替换 G2  
**Dependencies:** Task 1  
**Files:**
- Modify: `src/Schemata.Security.Foundation/Extensions/SchemataBuilderExtensions.cs:17-38`
- Modify: `src/Schemata.Security.Foundation/Stores/RepositoryTokenStore.cs:30`
- Modify: `src/Schemata.Security.Skeleton/Services/ITokenStore.cs:23`
- Modify: `src/Schemata.Authorization.Foundation/Handlers/*.cs`（所有 `ITokenStore<SchemataToken>` 使用点）
- Modify: `src/Schemata.Authorization.Foundation/Authentication/SchemataAuthenticationHandler.cs:177`
- Test: `tests/Schemata.Security.Tests/`
- Test: `tests/Schemata.Authorization.Integration.Tests/TokenLifecycleShould.cs`

**Interfaces:**
```csharp
public static SchemataBuilder UseSecurity<TSecurity, TToken>(
    this SchemataBuilder builder,
    Action<SchemataSecurityBuilder>? configure = null)
    where TSecurity : SchemataSecurity, new()
    where TToken : SchemataToken, new();
```
- `RepositoryTokenStore<TToken>` closes on `TToken`; default `UseSecurity()` forwards to `<SchemataSecurity,SchemataToken>`.

- [ ] **Step 1: 泛型入口/store**
  添加 `TToken` 参数；`RepositoryTokenStore<TToken>` 所有 `new SchemataToken`、`IRepository<SchemataToken>`、`ITokenStore<SchemataToken>` 改为 `TToken`。
- [ ] **Step 2: 传播到 Authorization**
  通过 LSP references 更新所有 `ITokenStore<SchemataToken>` 消费点到 `ITokenStore<TToken>`；Authorization feature 从 Security 接收同一闭合 token 类型，不定义第二套 token。
- [ ] **Step 3: 自定义 Token 测试**
  新增 `MyToken : SchemataToken`，通过 `UseSecurity<MySecurity,MyToken>()` 完成 publication/redeem/rotation/revocation，断言 EF/LinqToDB 行和失败路径。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Security.Tests/Schemata.Security.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Security.Tests/Schemata.Security.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RepositoryTokenStore"`
  Run: `dotnet test tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TokenLifecycleShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Security.Foundation src/Schemata.Security.Skeleton src/Schemata.Authorization.Foundation tests/Schemata.Security.Tests tests/Schemata.Authorization.Integration.Tests/TokenLifecycleShould.cs`
  `git commit -m "feat(Security)!: close token entities through UseSecurity"`

### Task 12: Identity 七个实体泛型

**Issues:** 通用实体替换 G2  
**Dependencies:** Task 11（Authorization.Identity 使用同一 `TUser`）  
**Files:**
- Modify: `src/Schemata.Identity.Foundation/Extensions/SchemataBuilderExtensions.cs:23-76`
- Modify: `src/Schemata.Identity.Foundation/SchemataIdentityBuilder.cs`
- Modify: `src/Schemata.Identity.Foundation/Features/SchemataIdentityFeature.cs`
- Modify: `src/Schemata.Identity.Skeleton/Stores/SchemataUserStore.cs:73-78`
- Modify: `src/Schemata.Identity.Skeleton/Stores/SchemataRoleStore.cs:20-65`
- Modify: `src/Schemata.Identity.Skeleton/Mutations/UserResourceMutation.cs:25-37`
- Modify: `src/Schemata.Identity.Skeleton/Mutations/RoleResourceMutation.cs:23-37`
- Modify: `src/Schemata.Identity.Http/Features/SchemataIdentityHttpFeature.cs:33-34`
- Modify: `src/Schemata.Authorization.Identity/IdentitySubjectProvider.cs:30`
- Test: `tests/Schemata.Identity.Integration.Tests/`
- Test: `tests/Schemata.Authorization.Tests/IdentityBridgeShould.cs`

**Interfaces:**
```csharp
public static SchemataBuilder UseIdentity<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole>(
    this SchemataBuilder builder,
    Action<SchemataIdentityBuilder<TUser, TRole>>? configure = null)
    where TUser : SchemataUser, new()
    where TRole : SchemataRole, new()
    where TUserClaim : SchemataUserClaim, new()
    where TRoleClaim : SchemataRoleClaim, new()
    where TUserLogin : SchemataUserLogin, new()
    where TUserToken : SchemataUserToken, new()
    where TUserRole : SchemataUserRole, new();
```
- Default `UseIdentity()` forwards to the seven built-in types; custom `TUserStore/TRoleStore` overload remains but closes the auxiliary entities.

- [ ] **Step 1: 泛型入口与 builder**
  添加七参数 `UseIdentity`；builder 持有七个实体类型，默认 store/mutation/HTTP registration 全部闭合。
- [ ] **Step 2: 关闭 stores/mutations**
  `SchemataUserStore`、`SchemataRoleStore`、`UserResourceMutation`、`RoleResourceMutation`、`SchemataSignInManager` 删除硬编码 `SchemataUserClaim`/`SchemataRoleClaim`/`SchemataUserLogin`/`SchemataUserToken`/`SchemataUserRole`，使用泛型参数。
- [ ] **Step 3: 传播 Authorization.Identity**
  `IdentitySubjectProvider<TUser>`、`PairwiseSubjectTranslator`、HTTP feature 与 resource registration 使用同一 `TUser/TRole`；没有第二套 user/claim 实体。
- [ ] **Step 4: 自定义七实体测试**
  新增七个 `My*` 子类型，通过 `UseIdentity<...>()` 建立 host，完成 register/login/claim/role/token 操作；断言 EF/LinqToDB 映射与关联表。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Identity.Integration.Tests/Schemata.Identity.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Identity.Integration.Tests/Schemata.Identity.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LoginShould|FullyQualifiedName~IdentityAssociationsShould"`
  Run: `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~IdentityBridgeShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Identity.Foundation src/Schemata.Identity.Skeleton src/Schemata.Identity.Http src/Schemata.Authorization.Identity tests/Schemata.Identity.Integration.Tests tests/Schemata.Authorization.Tests/IdentityBridgeShould.cs`
  `git commit -m "feat(Identity)!: close identity entity set through UseIdentity"`

### Task 13: Authorization 四实体泛型

**Issues:** 通用实体替换 G2  
**Dependencies:** Task 11、Task 12  
**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Extensions/SchemataBuilderExtensions.cs:32-57`
- Modify: `src/Schemata.Authorization.Foundation/SchemataAuthorizationBuilder.cs`
- Modify: `src/Schemata.Authorization.Foundation/Features/SchemataAuthorizationFeature.cs`
- Modify: `src/Schemata.Authorization.Foundation/Mutations/ApplicationResourceMutation.cs:106-129`
- Modify: `src/Schemata.Authorization.Foundation/Services/PairwiseSubjectTranslator.cs:48-57`
- Modify: `src/Schemata.Authorization.Foundation/Services/BackChannelLogoutService.cs:40`
- Modify: `src/Schemata.Authorization.Http/Features/SchemataAuthorizationHttpFeature.cs:37-39`
- Test: `tests/Schemata.Authorization.Integration.Tests/ApplicationCascadeShould.cs`
- Test: `tests/Schemata.Authorization.Tests/`

**Interfaces:**
```csharp
public static SchemataBuilder UseAuthorization<TApplication, TAuthorization, TScope, TSubjectMapping>(
    this SchemataBuilder builder,
    Action<SchemataAuthorizationBuilder<TApplication, TAuthorization, TScope>>? configure = null)
    where TApplication : SchemataApplication, new()
    where TAuthorization : SchemataAuthorization, new()
    where TScope : SchemataScope, new()
    where TSubjectMapping : SchemataSubjectMapping, new();
```
- Default `UseAuthorization()` forwards to four built-in types; `TToken` comes from Task 11.

- [ ] **Step 1: 泛型入口/builder**
  添加 `TSubjectMapping` 参数；builder/feature 关闭四实体。默认入口只调用泛型实现。
- [ ] **Step 2: 关闭 mutation/translator/logout**
  `ApplicationResourceMutation<TApplication,TAuthorization,TSubjectMapping>`、`PairwiseSubjectTranslator<TApplication,TSubjectMapping>`、`BackChannelLogoutService<TApplication>` 使用闭合类型；删除 `IRepository<SchemataSubjectMapping>` 硬编码。
- [ ] **Step 3: 传播 HTTP**
  `SchemataAuthorizationHttpFeature` 使用同一 `TApplication/TAuthorization/TScope`；token resource 使用 Task 11 的 `TToken`。
- [ ] **Step 4: 自定义类型测试**
  用现有 `Application : SchemataApplication, ISoftDelete`、`Grant : SchemataAuthorization, ISoftDelete` 与自定义 `Scope/SubjectMapping` 走 `UseAuthorization<...>`，完成 client registration/cascade/subject mapping；断言没有默认实体行。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ApplicationCascadeShould|FullyQualifiedName~ApplicationLifecycleShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Authorization.Foundation src/Schemata.Authorization.Http tests/Schemata.Authorization.Integration.Tests tests/Schemata.Authorization.Tests`
  `git commit -m "feat(Authorization)!: close authorization entities through UseAuthorization"`

### Task 14: Tenancy TenantHost 泛型

**Issues:** 通用实体替换 G2  
**Dependencies:** Task 12  
**Files:**
- Modify: `src/Schemata.Tenancy.Foundation/Extensions/SchemataBuilderExtensions.cs:27-35`
- Modify: `src/Schemata.Tenancy.Foundation/SchemataTenancyBuilder.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Features/SchemataTenancyFeature.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/GetTenantHostsHandler.cs:13`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/FindTenantByIdHandler.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/SetTenantHostsHandler.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/CreateTenantHandler.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/UpdateTenantHandler.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/SetTenantDisplayNameHandler.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/SetTenantLocalizedDisplayNamesHandler.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/FindTenantByHostHandler.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Handlers/DeleteTenantHandler.cs`
- Test: `tests/Schemata.Tenancy.Tests/TenantRepositoryRegistrationShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/`

**Interfaces:**
```csharp
public static SchemataBuilder UseTenancy<TTenant, TTenantHost>(
    this SchemataBuilder builder,
    Action<SchemataTenancyBuilder<TTenant>>? configure = null)
    where TTenant : SchemataTenant, new()
    where TTenantHost : SchemataTenantHost, new();
```
- Default `UseTenancy()` forwards to `<SchemataTenant,SchemataTenantHost>`.

- [ ] **Step 1: 泛型入口/builder**
  添加 `TTenantHost`；builder/feature/handlers 关闭 tenant 与 host 两类型。默认入口只调用泛型实现。
- [ ] **Step 2: 关闭 handlers**
  `GetTenantHostsHandler<TTenant,TTenantHost>` 不再硬编码 `IRepository<SchemataTenantHost>`；所有 tenant handler 使用 `TTenant`/`TTenantHost`。
- [ ] **Step 3: 自定义 host 测试**
  新增 `Catalog : SchemataTenant, ITenant` 与 `CatalogHost : SchemataTenantHost`，通过 `UseTenancy<Catalog,CatalogHost>()` 完成 CRUD/host lookup；默认类型测试保持。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Tenancy.Tests/Schemata.Tenancy.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Tenancy.Tests/Schemata.Tenancy.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantRepositoryRegistrationShould"`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantRepositoryShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Tenancy.Foundation tests/Schemata.Tenancy.Tests tests/Schemata.Tenancy.Integration.Tests`
  `git commit -m "feat(Tenancy)!: close tenant host through UseTenancy"`

### Task 15: Push/Insight/Report 实体闭合

**Issues:** 通用实体替换 G2  
**Dependencies:** Task 9（scheduling bridge）  
**Files:**
- Modify: `src/Schemata.Push.Foundation/Extensions/SchemataBuilderExtensions.cs:17`
- Modify: `src/Schemata.Push.Foundation/SchemataPushBuilder.cs`
- Modify: `src/Schemata.Push.Foundation/Features/SchemataPushFeature.cs`
- Modify: `src/Schemata.Push.Foundation/Handlers/AddPushSubscriptionHandler.cs:47`
- Modify: `src/Schemata.Push.Http/Features/SchemataPushHttpFeature.cs`
- Modify: `src/Schemata.Insight.Foundation/Extensions/SchemataBuilderExtensions.cs:22-25`
- Modify: `src/Schemata.Insight.Foundation/SchemataInsightBuilder.cs:89-91`
- Modify: `src/Schemata.Insight.Foundation/Features/SchemataInsightFeature.cs`
- Modify: `src/Schemata.Insight.Http/Features/SchemataInsightHttpFeature.cs`
- Verify: `src/Schemata.Report.Foundation/Extensions/SchemataBuilderExtensions.cs:21-39`
- Test: `tests/Schemata.Push.Tests/`, `tests/Schemata.Push.Http.Integration.Tests/`
- Test: `tests/Schemata.Insight.Tests/`, `tests/Schemata.Insight.Http.Integration.Tests/`
- Test: `tests/Schemata.Report.Tests/ReportCustomImplementationShould.cs`

**Interfaces:**
```csharp
public static SchemataPushBuilder UsePush<TPushSubscription>(
    this SchemataBuilder builder)
    where TPushSubscription : SchemataPushSubscription, new();

public static SchemataInsightBuilder UseInsight<TInsightSource>(
    this SchemataBuilder builder,
    Action<SchemataInsightBuilder>? configure = null)
    where TInsightSource : SchemataInsightSource, new();
```
- Report already has `UseReport<TReport,TSnapshot,TChunk>`; do not add a second path.

- [ ] **Step 1: Push 泛型**
  `UsePush<TPushSubscription>` 关闭 feature/repository/handler/HTTP；默认 `UsePush()` 转发到内置类型。
- [ ] **Step 2: Insight 泛型**
  `UseInsight<TInsightSource>` 关闭 catalog/builder/HTTP；默认入口转发到内置类型。
- [ ] **Step 3: Report 保持**
  确认 `UseReport()` 转发到 `UseReport<SchemataReport,SchemataReportSnapshot,SchemataReportSnapshotChunk>()`；不新增泛型表面。
- [ ] **Step 4: 自定义类型测试**
  新增自定义 push subscription 与 insight source，通过 `UsePush<...>()`/`UseInsight<...>()` 完成 EF/LinqToDB 映射和主要操作；Report 使用现有 `OtherReport`/`ScheduleReport` 测试。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Push.Tests/Schemata.Push.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Push.Tests/Schemata.Push.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~PushSubscription"`
  Run: `dotnet test tests/Schemata.Insight.Tests/Schemata.Insight.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~InsightSourceCatalog"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Push.Foundation src/Schemata.Push.Http src/Schemata.Insight.Foundation src/Schemata.Insight.Http tests/Schemata.Push.Tests tests/Schemata.Insight.Tests`
  `git commit -m "feat(Push,Insight)!: close entities through Use* entries"`

---

## Wave B：Tenancy 边界

### Task 16: 默认实体移除 ITenant

**Issues:** #350  
**Dependencies:** Task 8、Task 9、Task 10  
**Files:**
- Modify: `src/Schemata.Actor.Skeleton/Entities/SchemataActor.cs:19-26`
- Modify: `src/Schemata.Flow.Skeleton/Entities/SchemataProcess.cs:15-26`
- Modify: `src/Schemata.Flow.Skeleton/Entities/SchemataProcessToken.cs:21-27`
- Modify: `src/Schemata.Flow.Skeleton/Entities/SchemataProcessTransition.cs:16-22`
- Modify: `src/Schemata.Flow.Skeleton/Entities/SchemataProcessCompensation.cs:14-17`
- Modify: `src/Schemata.Flow.Skeleton/Entities/SchemataProcessParticipant.cs:13-20`
- Modify: `src/Schemata.Flow.Skeleton/Entities/SchemataProcessSource.cs:16-23`
- Modify: `src/Schemata.Flow.Repository/SchemataFlowEffectIntent.cs:21-27`
- Modify: `src/Schemata.Scheduling.Skeleton/Entities/SchemataJob.cs:16-21`
- Modify: `src/Schemata.Scheduling.Skeleton/Entities/SchemataJobExecution.cs:25-28`
- Test: `tests/Schemata.Flow.Integration.Tests/FlowTenantRepositoryShould.cs`
- Test: `tests/Schemata.Scheduling.Integration.Tests/`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantRepositoryShould.cs`

**Interfaces:**
- Default entities keep `ICanonicalName`, `IIdentifier`, `IStateful`, timestamps, soft-delete; remove `ITenant`, `TenantUid`, tenant-specific indexes.
- Custom subtype implements `ITenant` and is registered through the generic `Use*` entry.

- [ ] **Step 1: 删除默认 trait/列/索引**
  从十个实体删除 `ITenant`、`TenantUid` 与 `TenantUid` 相关索引；保留非租户索引。同步 EF/LinqToDB mapping 和任何 `TenantUid` 默认赋值。
- [ ] **Step 2: 迁移测试类型**
  `FlowTenantRepositoryShould` 改用自定义 `TenantProcess : SchemataProcess, ITenant` 等子类型，通过 `UseFlow<...>` 注册；不直接构造默认实体。
- [ ] **Step 3: 写隔离测试**
  自定义 `ITenant` 类型经 feature 注册后由 repository advisor 隔离；host 只见 host 行；跨租户 update/remove 明确失败；默认实体测试不依赖 `ITenant`。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Flow.Integration.Tests/Schemata.Flow.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Flow.Integration.Tests/Schemata.Flow.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~FlowTenantRepositoryShould"`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantRepositoryShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Actor.Skeleton src/Schemata.Flow.Skeleton src/Schemata.Flow.Repository src/Schemata.Scheduling.Skeleton tests/Schemata.Flow.Integration.Tests/FlowTenantRepositoryShould.cs tests/Schemata.Tenancy.Integration.Tests`
  `git commit -m "refactor(Tenancy)!: remove tenant trait from default entities"`

### Task 17: Repository 两模式与 Tenancy 依赖收敛

**Issues:** #349  
**Dependencies:** Task 16、Task 20  
**Files:**
- Modify: `src/Schemata.Tenancy.Foundation/SchemataTenancyBuilder.cs:32-70`
- Modify: `src/Schemata.Tenancy.Foundation/Repository/TenantRepositoryBuilder.cs:31-73`
- Modify: `src/Schemata.Tenancy.Foundation/Repository/TenantDatabaseExtensions.cs:32-53`
- Modify: `src/Schemata.Tenancy.Foundation/Repository/TenantMutation.cs:11-32`
- Modify: `src/Schemata.Tenancy.Foundation/Schemata.Tenancy.Foundation.csproj:20-24`
- Modify: `src/Schemata.Entity.Repository/IRepository.cs`、`RepositoryBase.cs`（query suppression 参数）
- Test: `tests/Schemata.Tenancy.Tests/TenantRepositoryRegistrationShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantRepositoryShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantDatabaseShould.cs`

**Interfaces:**
- `UseDatabase<TTenant,TEntity,TRepository>` remains explicit per-entity source; entity needs no `ITenant`.
- Shared table: open-generic `AdviceAddTenant<TEntity>`, `AdviceBuildQueryTenant<TEntity>`, `AdviceUpdateTenant<TEntity>`, `AdviceRemoveTenant<TEntity>`; non-`ITenant` returns `Continue`.
- Delete `UseSharedTable` and `IsRegistrationProbe`; `UseRepositories` resolves host/tenant binding from DI, no throwaway collection or `Guid.NewGuid()` placeholder.

- [ ] **Step 1: 改造 `UseRepositories`**
  删除 harvest lambda、throwaway `ServiceCollection`、`Guid.NewGuid()` 占位和 `IsRegistrationProbe` 分支。Host/tenant binding 通过现有 DI 注册解析；`UseRepositories` 只登记 open-generic advisors。
- [ ] **Step 2: 删除 `UseSharedTable`**
  删除 `TenantRepositoryBuilder.UseSharedTable` 及类型限制；`UseDatabase` 保持显式 source 配置。Shared-table 隔离只由 open-generic advisors 对 `ITenant` 实体执行。
- [ ] **Step 3: 断依赖**
  从 `Schemata.Tenancy.Foundation.csproj` 删除 `Schemata.Entity.Cache` 与 `Schemata.Scheduling.Skeleton` 引用；tenant handler 使用 `Entity.Repository` 的显式 query suppression 参数。
- [ ] **Step 4: 写两模式测试**
  分库实例不要求 `ITenant`；shared-table 自定义 `ITenant` 实体无需额外 builder 调用即可隔离；host/tenant 行可见性、跨租户 update/remove 失败；`SchemataTenancyBuilder.cs:33-36` 与 `TenantRepositoryBuilder.cs:50` 分支消失。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Tenancy.Tests/Schemata.Tenancy.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Tenancy.Tests/Schemata.Tenancy.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantRepositoryRegistrationShould"`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantRepositoryShould|FullyQualifiedName~TenantDatabaseShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Tenancy.Foundation src/Schemata.Entity.Repository tests/Schemata.Tenancy.Tests tests/Schemata.Tenancy.Integration.Tests`
  `git commit -m "refactor(Tenancy)!: derive repository isolation from entity traits"`

### Task 18: Scheduling 移除 tenant 探测

**Issues:** #349  
**Dependencies:** Task 9、Task 16  
**Files:**
- Delete: `src/Schemata.Tenancy.Foundation/Scheduling/TenantJobExecutionAdapter.cs`
- Delete: `src/Schemata.Tenancy.Foundation/Scheduling/TenantSchedulingExtensions.cs`
- Modify: `src/Schemata.Tenancy.Foundation/TenancyConstants.cs:10-12`
- Modify: `src/Schemata.Tenancy.Foundation/Schemata.Tenancy.Foundation.csproj:24`
- Modify: `src/Schemata.Scheduling.Foundation/JobExecutionDispatcher.cs`（remove tenant enumeration）
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantJobShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantSchedulingShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantReminderShould.cs`

**Interfaces:**
- Scheduling sources contain no `TenantContext`, tenant catalog, tenant scope creation, or per-tenant dispatcher start.
- Host dispatcher reads only host rows. Per-tenant scheduling is registered by the user in the tenant container.

- [ ] **Step 1: 删除 adapter/keyed backend**
  删除 `TenantJobExecutionAdapter`、`TenantSchedulingExtensions`、`TenancyConstants.Scheduling.Backend` 和 `Scheduling.Skeleton` 引用。
- [ ] **Step 2: 清理 Scheduling**
  删除 `JobExecutionDispatcher` 中 tenant catalog 枚举、scope 创建、keyed store 解析和 `MessageContext` tenant 恢复；host dispatcher 只处理 host 行。
- [ ] **Step 3: 更新 tenant 测试**
  `TenantJobShould`/`TenantSchedulingShould`/`TenantReminderShould` 改为在 tenant 注册中显式提供 scheduler/dispatcher；断言 host 调度只读 host 行，tenant 调度由 tenant 容器独立运行。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantJobShould|FullyQualifiedName~TenantSchedulingShould|FullyQualifiedName~TenantReminderShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Tenancy.Foundation src/Schemata.Scheduling.Foundation tests/Schemata.Tenancy.Integration.Tests`
  `git commit -m "refactor(Tenancy)!: remove scheduling tenant probing"`

### Task 19: Tenancy 组合期 hack 收敛

**Issues:** #338  
**Dependencies:** Task 8、Task 17  
**Files:**
- Modify: `src/Schemata.Tenancy.Foundation/Middlewares/TenantPolicyEvaluator.cs:13-23`
- Modify: `src/Schemata.Tenancy.Foundation/Features/SchemataTenancyFeature.cs:54-55,81`
- Modify: `src/Schemata.Tenancy.Foundation/State/TenantAntiforgeryRegistration.cs:67-80`
- Modify: `src/Schemata.Tenancy.Messaging/TenantMessagingExtensions.cs:11-44`
- Modify: `src/Schemata.Tenancy.Actor/TenantActorExtensions.cs:14-26`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantAntiforgeryShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantDispatcherShould.cs`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantActorShould.cs`

**Interfaces:**
- No `inner.GetType() == typeof(PolicyEvaluator)`; no descriptor scan/replace; no container-build probe scope; no `InternalsVisibleTo`.
- Tenant policy/controller/dispatcher/actor system resolved through explicit keyed/optional registrations.

- [ ] **Step 1: Policy/Controller**
  `TenantPolicyEvaluator` 通过 keyed inner provider 构造，不再比较具体类型。`SchemataTenancyFeature` 删除 `Decorate` descriptor 扫描/替换，使用 Core 暴露的显式 decorator/registration seam；`IControllerFactory` 同样显式注册。
- [ ] **Step 2: Antiforgery**
  删除 `IStatePipelineProvider` descriptor 删除循环；状态分支由单一 provider 注册扩展贡献。保留现有 boundary-name 校验行为。
- [ ] **Step 3: Messaging**
  `TenantMessagingExtensions.UseMessaging` 删除构建期 probe scope 和同步 dispose；dispatcher aliasing 由注册期契约决定。
- [ ] **Step 4: Actor**
  `Tenancy.Actor` 不替换不属于自己的 descriptor；通过 Actor 公开 factory/runtime seam 注册 `TenantActorSystem<TActor>`。
- [ ] **Step 5: 验证**
  断言无 `GetType()` 比较、无构建期 probe、无 descriptor 删除；tenant 注册仍解析到正确 policy/controller/dispatcher/actor system。
- [ ] **Step 6: 单次验证与 commit**
  Run: `dotnet build tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Tenancy.Integration.Tests/Schemata.Tenancy.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantAntiforgeryShould|FullyQualifiedName~TenantDispatcherShould|FullyQualifiedName~TenantActorShould"`
  `git add src/Schemata.Tenancy.Foundation src/Schemata.Tenancy.Messaging src/Schemata.Tenancy.Actor tests/Schemata.Tenancy.Integration.Tests`
  `git commit -m "refactor(Tenancy)!: replace composition hacks with explicit registrations"`

### Task 20: Repository suppression 显式参数

**Issues:** #336  
**Dependencies:** 无（F2 之前）  
**Files:**
- Modify: `src/Schemata.Entity.Repository/IRepository.cs:55-79`
- Modify: `src/Schemata.Entity.Repository/RepositoryBase.cs:496-500`
- Modify: `src/Schemata.Entity.Repository/Extensions/OwnerExtensions.cs:21-33`
- Modify: `src/Schemata.Entity.Cache/Extensions/RepositoryExtensions.cs:26-41`
- Modify: `src/Schemata.Entity.Repository/Advisors/AdviceValidateResourceReferenceExistence.cs:151`
- Modify: `src/Schemata.Flow.Foundation/FlowSourceReadScope.cs:14-16`
- Modify: `src/Schemata.Flow.Foundation/ProcessPersistence.cs:255-257`
- Modify: `src/Schemata.Tenancy.Foundation/Repository/TenantMutation.cs:17`
- Modify: `src/Schemata.Resource.Foundation/Handlers/*.cs`（所有 suppression 调用点）
- Test: `tests/Schemata.Flow.Integration.Tests/SourceOwnerSuppressionShould.cs`
- Test: `tests/Schemata.Flow.Tests/FlowSourceReadScopeShould.cs`
- Test: `tests/Schemata.Resource.Tests/`

**Interfaces:**
- Replace marker methods with explicit operation parameters on repository query/mutation APIs:
```csharp
Task<T?> FirstOrDefaultAsync<T>(
    Expression<Func<T, bool>> predicate,
    QueryOptions options,
    CancellationToken ct = default);

public readonly record struct QueryOptions(
    bool IgnoreSoftDelete = false,
    bool IgnoreOwner = false,
    bool IgnoreCache = false,
    bool IgnoreCacheEviction = false);
```
- `Add/Update` validation suppression becomes explicit `MutationOptions`; no `AdviceContext.Use/Has` marker.

- [ ] **Step 1: 增加显式 options**
  在 `IRepository`/`RepositoryBase` 增加 `QueryOptions`/`MutationOptions` 参数；删除五个 `Suppress*` 方法及 owner/cache extension marker。
- [ ] **Step 2: 迁移调用点**
  通过 LSP references 迁移 `FlowSourceReadScope`、`ProcessPersistence`、reference existence、Undelete/Delete、tenant resolution、Resource handlers、Scheduling/Push 调用点；每个调用点直接传 options，不打开嵌套 scope。
- [ ] **Step 3: 删除 marker 分支**
  repository advisors 读取显式参数；删除 `AdviceContext.Use/Has` marker 分支与 marker 类型。嵌套/并行调用互不影响。
- [ ] **Step 4: 写 suppression 测试**
  `SourceOwnerSuppressionShould` 覆盖 owner/soft-delete 读取；`FlowSourceReadScopeShould` 覆盖嵌套与并行；Resource integration 覆盖 Undelete/Delete/Expunge 的显式 suppression；删除只验证 marker key 的测试。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Flow.Tests/Schemata.Flow.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Flow.Tests/Schemata.Flow.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~FlowSourceReadScopeShould"`
  Run: `dotnet test tests/Schemata.Flow.Integration.Tests/Schemata.Flow.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~SourceOwnerSuppressionShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Entity.Repository src/Schemata.Entity.Cache src/Schemata.Flow.Foundation src/Schemata.Tenancy.Foundation/Repository src/Schemata.Resource.Foundation tests/Schemata.Flow.Tests tests/Schemata.Flow.Integration.Tests`
  `git commit -m "refactor(Entity)!: pass suppression explicitly"`

### Task 21: Tenant 边界审计、Cache/State 验收与文档

**Issues:** #349、#350、B4、B7  
**Dependencies:** Task 16–20  
**Files:**
- Move: `src/Schemata.Abstractions/Tenancy/TenantContext.cs` → `src/Schemata.Tenancy.Skeleton/Tenancy/TenantContext.cs`
- Move: `src/Schemata.Abstractions/Tenancy/TenantIdentity.cs` → `src/Schemata.Tenancy.Skeleton/Tenancy/TenantIdentity.cs`
- Move: `src/Schemata.Abstractions/Exceptions/TenantResolveException.cs` → `src/Schemata.Tenancy.Skeleton/Tenancy/TenantResolveException.cs`
- Modify: `src/Schemata.Tenancy.Skeleton/Schemata.Tenancy.Skeleton.csproj`
- Modify: `src/Schemata.Tenancy.Caching/TenantCacheBuilder.cs`
- Modify: `docs/guides/multi-tenancy.md`
- Modify: `docs/guides/flow.md`
- Modify: `docs/guides/actor.md`
- Modify: `docs/documents/scheduling/persistence.md`
- Test: `tests/Schemata.Abstractions.Tests/`
- Test: `tests/Schemata.Tenancy.Caching.Integration.Tests/`
- Test: `tests/Schemata.Tenancy.Integration.Tests/TenantCookieShould.cs`

**Interfaces:**
- `Schemata.Abstractions` keeps `ITenant` and generic error constants only.
- `Schemata.Tenancy.Skeleton` references `Schemata.Abstractions`/`Schemata.Common` directly and owns `TenantContext`/`TenantIdentity`/`TenantResolveException`.
- `UseCache` is the only tenant cache namespace wrapper; Tenancy enable does not wrap host `ICacheProvider`.

- [ ] **Step 1: 移动运行时类型**
  移动三个类型到 `Schemata.Tenancy.Skeleton`，更新命名空间/using；删除 `Schemata.Abstractions` 中的旧文件。补齐 `Tenancy.Skeleton` 直接项目引用，移除未使用的 `Entity.Repository` 引用。
- [ ] **Step 2: 清理非 Tenancy 注释/文档**
  非 Tenancy XML 注释只描述通用扩展入口；tenant 注册说明移到 Tenancy 文档。更新四个文档中的 default `ITenant`/adapter 描述。
- [ ] **Step 3: Cache/State 验收**
  断言只有显式 `UseCache` 的 tenant 获得 namespace；host `ICacheProvider` 不被自动包装；cookie/bearer/session/antiforgery scheme 仍由显式注册决定。
- [ ] **Step 4: 边界审计**
  全 `src/` grep `TenantContext`/`TenantUid`/`CreateAsyncScope`，确认非 Tenancy component 无 tenant 探测/scope 选择；`Schemata.Abstractions` 仅保留 `ITenant`。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Tenancy.Caching.Integration.Tests/Schemata.Tenancy.Caching.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Tenancy.Caching.Integration.Tests/Schemata.Tenancy.Caching.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantCacheBridgeShould|FullyQualifiedName~TenantQueryCacheShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Abstractions src/Schemata.Tenancy.Skeleton src/Schemata.Tenancy.Caching docs/guides docs/documents/scheduling/persistence.md tests/Schemata.Tenancy.Caching.Integration.Tests`
  `git commit -m "refactor(Tenancy)!: contain tenant runtime in Tenancy packages"`

---

## Wave C：一致性与债务

### Task 22: Active execution registry

**Issues:** #346  
**Dependencies:** 无  
**Files:**
- Create: `src/Schemata.Scheduling.Skeleton/Services/IActiveExecutionRegistry.cs`
- Create: `src/Schemata.Scheduling.Foundation/Runtime/ActiveExecutionRegistry.cs`
- Modify: `src/Schemata.Scheduling.Foundation/Extensions/SchemataSchedulingServiceCollectionExtensions.cs:29`
- Modify: `src/Schemata.Scheduling.Foundation/JobExecutionDispatcher.cs:55-57`
- Modify: `src/Schemata.Scheduling.Foundation/ExecutionLease.cs:21-32`
- Modify: `src/Schemata.Scheduling.Foundation/DefaultOperationService.cs:129,188`
- Test: `tests/Schemata.Scheduling.Tests/`
- Test: `tests/Schemata.Scheduling.Integration.Tests/`

**Interfaces:**
```csharp
public interface IActiveExecutionRegistry
{
    IDisposable Register(string executionName, CancellationTokenSource source);
    bool TryRemove(string executionName, out CancellationTokenSource? source);
    bool Cancel(string executionName);
}
```
- Registry is a singleton owned by Scheduling; one implementation.

- [ ] **Step 1: 新建 registry**
  实现 typed registry，替换 DI 中的 `ConcurrentDictionary<string, CancellationTokenSource>`。`Register` 返回 ownership handle；`TryRemove`/`Cancel` 只允许赢得移除的路径释放 CTS。
- [ ] **Step 2: 迁移消费者**
  `JobExecutionDispatcher`、`ExecutionLease`、`DefaultOperationService` 通过 `IActiveExecutionRegistry` 注册/移除/取消；dispatcher 删除 `GetService(...) ?? new()` fallback。
- [ ] **Step 3: 注册**
  `SchemataSchedulingServiceCollectionExtensions` 只注册 `IActiveExecutionRegistry`；不注册字典类型。
- [ ] **Step 4: 写竞态测试**
  覆盖并发 register/remove/cancel、取消后移除、dispatcher/lease/operation service 共享同一 registry；断言无未释放 CTS。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Scheduling.Tests/Schemata.Scheduling.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Scheduling.Tests/Schemata.Scheduling.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ActiveExecutionRegistry|FullyQualifiedName~JobExecutionDispatcherShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Scheduling.Skeleton/Services/IActiveExecutionRegistry.cs src/Schemata.Scheduling.Foundation tests/Schemata.Scheduling.Tests`
  `git commit -m "refactor(Scheduling): own active executions through a typed registry"`

### Task 23: 统一 handled output

**Issues:** #347  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Abstractions/Advisors/HandledResponse.cs:11`
- Create: `src/Schemata.Abstractions/Advisors/IHandledResponse.cs`
- Modify: `src/Schemata.Resource.Foundation/Advisors/ResourceMethodExecution.cs:19`
- Modify: `src/Schemata.Identity.Skeleton/IdentityOperationExecution.cs:13`
- Modify: `src/Schemata.Identity.Foundation/Handlers/IdentityOperationHandler.*.cs`
- Modify: `src/Schemata.Identity.Foundation/SchemataSignInManager.cs:94`
- Modify: `src/Schemata.Resource.Foundation/Advisors/*.cs`
- Test: `tests/Schemata.Resource.Tests/`
- Test: `tests/Schemata.Identity.Tests/`

**Interfaces:**
```csharp
public interface IHandledResponse<TResponse>
{
    TResponse? Response { get; }
    bool Handled { get; }
    void SetResponse(TResponse response);
}

public sealed class HandledResponse<TResponse> : IHandledResponse<TResponse>
    where TResponse : class;
```
- `ResourceMethodExecution<TEntity,TRequest,TResponse>` and `IdentityOperationExecution<TResponse>` expose the same `IHandledResponse<TResponse>` contract.

- [ ] **Step 1: 定义接口**
  `HandledResponse<TResponse>` 实现 `IHandledResponse<TResponse>`；保留现有 `Response`/`Handled` 行为。
- [ ] **Step 2: 统一执行载体**
  `ResourceMethodExecution<...>` 与 `IdentityOperationExecution<TResponse>` 实现/持有同一 handled-output 契约；标准 resource payload 使用同一接口。删除 Identity 每个操作的第二个 response carrier。
- [ ] **Step 3: 迁移消费者**
  Resource/Identity handlers 通过 `IHandledResponse<TResponse>` 读写；不保留 `TryGet<TResult>` 或 bag 往返。
- [ ] **Step 4: 写载体测试**
  覆盖 Resource custom method、标准 Create/Update、Identity manage/sign/reset/verify：每个操作只有一个 carrier，Handle/Block/Continue 结果一致。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~HandledResponse|FullyQualifiedName~ResourceMethodExecution"`
  Run: `dotnet test tests/Schemata.Identity.Tests/Schemata.Identity.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~IdentityOperationHandler"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Abstractions/Advisors src/Schemata.Resource.Foundation src/Schemata.Identity.Skeleton src/Schemata.Identity.Foundation tests/Schemata.Resource.Tests tests/Schemata.Identity.Tests`
  `git commit -m "refactor: unify handled response carriers"`

### Task 24: Canonical pattern/collection 注册期拒绝

**Issues:** #340  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Core/Building/ResourceRegistry.cs:98-196`
- Modify: `src/Schemata.Resource.Foundation/DefaultResourceTypeResolver.cs:82-115`
- Modify: `src/Schemata.Resource.Foundation/Extensions/ServiceCollectionExtensions.cs:288,349-360`
- Test: `tests/Schemata.Resource.Tests/`
- Test: `tests/Schemata.Report.Tests/ReportCapabilityConflictShould.cs`

**Interfaces:**
- Registration fails on duplicate normalized canonical pattern or collection name across distinct entities; no `DefaultResourceTypeResolver` first-wins.
- Typed/polymorphic `[ResourceReference]` resolution receives a determinate target.

- [ ] **Step 1: 注册期校验**
  `ResourceRegistry` 在 merge/install 时比较 normalized canonical pattern 与 collection name；重复时抛现有 conflict error，不依赖 resolver 覆盖。
- [ ] **Step 2: 移除 first-wins**
  `DefaultResourceTypeResolver.BuildCollectionIndex`/`BuildPatternTrie` 不再用 last-write-wins `Dictionary.Add` 覆盖；重复应在注册期已被拒绝，resolver 只做确定性查找。
- [ ] **Step 3: Reference 解析测试**
  覆盖重复 canonical pattern、重复 collection、typed reference、polymorphic reference；断言注册失败或解析到唯一目标，不再 first-wins。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ResourceRegistry|FullyQualifiedName~ResourceTypeResolver|FullyQualifiedName~ResourceReference"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Core/Building/ResourceRegistry.cs src/Schemata.Resource.Foundation/DefaultResourceTypeResolver.cs src/Schemata.Resource.Foundation/Extensions/ServiceCollectionExtensions.cs tests/Schemata.Resource.Tests`
  `git commit -m "fix(Resource): reject ambiguous canonical patterns at registration"`

### Task 25: AdviceRunner arity 生成

**Issues:** #341  
**Dependencies:** 无  
**Files:**
- Modify: `generators/Schemata.Advice.Generator/AdvicePipelineGenerator.cs:14-314`
- Delete: `src/Schemata.Advice/AdviceRunner.cs`、`AdviceRunner\`2.cs` … `AdviceRunner\`17.cs`
- Modify: `src/Schemata.Advice/Schemata.Advice.csproj`（generator 引用保持）
- Test: `tests/Schemata.Advice.Generator.Tests/`
- Test: `tests/Schemata.Advice.Tests/`

**Interfaces:**
- Generator emits all `AdviceRunner` arity classes (1–16) with the same `RunAsync` signatures used by `Advisor.For` and generated pipelines.
- No hand-written arity file remains.

- [ ] **Step 1: 生成 arity**
  扩展 `AdvicePipelineGenerator`，在 compilation 中生成 `AdviceRunner<...>` 类；签名与现有 16 个手写实现一致（`RunAsync` 参数顺序、返回值、`CancellationToken`）。
- [ ] **Step 2: 删除手写实现**
  删除 16 个 `AdviceRunner*.cs`；保留 `Advisor.For`、`AdvicePipeline<T>` 与非 arity 代码。
- [ ] **Step 3: 生成器测试**
  `Schemata.Advice.Generator.Tests` 断言生成源码包含 arity 1/8/16 类且可编译；`Schemata.Advice.Tests` 通过真实 generated pipeline 调用每个 arity，断言 advisor 顺序/短路行为不变。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Advice.Generator.Tests/Schemata.Advice.Generator.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Advice.Generator.Tests/Schemata.Advice.Generator.Tests.csproj -c Release --no-build`
  Run: `dotnet test tests/Schemata.Advice.Tests/Schemata.Advice.Tests.csproj -c Release --no-build`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add generators/Schemata.Advice.Generator src/Schemata.Advice tests/Schemata.Advice.Generator.Tests tests/Schemata.Advice.Tests`
  `git commit -m "refactor(Advice): generate arity runners"`

### Task 26: OIDC DCR response_type 表

**Issues:** #344  
**Dependencies:** 无  
**Files:**
- Modify: `src/Schemata.Authorization.Skeleton/AuthorizationConstants.cs:1821-1830`
- Modify: `src/Schemata.Authorization.Foundation/Services/RegistrationMetadataMapper.cs:38-40,395-399`
- Modify: `src/Schemata.Authorization.Foundation/Managers/SchemataApplicationMetadata.cs`
- Test: `tests/Schemata.Authorization.Integration.Tests/RegisterReplaceFlowShould.cs`
- Test: `tests/Schemata.Authorization.Tests/`

**Interfaces:**
```csharp
public static class ResponseTypeGrantTypes
{
    public static readonly IReadOnlyDictionary<string, string[]> Requirements =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [ResponseTypes.Code] = [GrantTypes.AuthorizationCode],
            [ResponseTypes.Token] = [GrantTypes.Implicit],
            [ResponseTypes.IdToken] = [GrantTypes.Implicit],
            ["code id_token"] = [GrantTypes.AuthorizationCode, GrantTypes.Implicit],
            ["code token"] = [GrantTypes.AuthorizationCode, GrantTypes.Implicit],
            ["code id_token token"] = [GrantTypes.AuthorizationCode, GrantTypes.Implicit],
        };
}
```
- Constants live in `AuthorizationConstants`; DCR, discovery and authorize use the same table.

- [ ] **Step 1: 补齐六项**
  在 `AuthorizationConstants` 增加 response_type→grant_types 表，覆盖 OIDC Registration §2 六项。`RegistrationMetadataMapper` 删除私有字典并引用该表。
- [ ] **Step 2: 对齐 discovery/authorize**
  `SchemataApplicationMetadata`/discovery/authorize 使用同一表；DCR 只接受表内组合。
- [ ] **Step 3: 写矩阵测试**
  `RegisterReplaceFlowShould` 覆盖六个 response_type 的 grant_types 映射与不支持组合拒绝；断言 discovery 与 authorize 的支持集合一致。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RegisterReplaceFlowShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Authorization.Skeleton/AuthorizationConstants.cs src/Schemata.Authorization.Foundation tests/Schemata.Authorization.Integration.Tests`
  `git commit -m "fix(Authorization): complete DCR response type mapping"`

### Task 27: CallActivity 当前 activation 匹配

**Issues:** #285  
**Dependencies:** Task 10  
**Files:**
- Modify: `src/Schemata.Flow.Skeleton/Entities/SchemataProcessToken.cs`（add `CurrentCallActivation`）
- Modify: `src/Schemata.Flow.Bpmn/Runtime/SubProcesses/CallActivityExecutor.cs:35-167`
- Modify: `src/Schemata.Flow.Bpmn/BpmnEngine.cs`（spawn transition/canonical name）
- Test: `tests/Schemata.Flow.Bpmn.Tests/BpmnEngine_CallActivityShould.cs`
- Test: `tests/Schemata.Flow.Integration.Tests/CallActivityChildRuntimeShould.cs`

**Interfaces:**
- `EnterAsync` creates the spawn transition with a unique activation canonical name and stores it in the parent token's `CurrentCallActivation`.
- `TryCompleteAsync` requires the activation name and matches the spawn transition by that name, not `FirstOrDefault(parentToken + activity name)`.
- Old activation completion is rejected after re-activation.

- [ ] **Step 1: 持久化 activation**
  `SchemataProcessToken` 增加 `CurrentCallActivation`（nullable string；无 tenant 字段）。`EnterAsync` 生成唯一 spawn transition/canonical name，写入 parent token。
- [ ] **Step 2: 精确匹配**
  `TryCompleteAsync` 签名接受 activation name；查询 `Process == parent && Token == parentToken && CanonicalName == activation`，不按 activity name 取 FirstOrDefault；完成后清除 token 的 activation。
- [ ] **Step 3: 迁移调用者**
  Bpmn engine、child completion handler 传递 activation name；旧 completion 找不到当前 activation 时拒绝，不重新绑定。
- [ ] **Step 4: 写重激活测试**
  `BpmnEngine_CallActivityShould` 覆盖 spawn transition 写入与 parent token park；`CallActivityChildRuntimeShould` 覆盖当前 activation 完成、旧 activation completion 拒绝、重新激活后只完成新 child。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Flow.Bpmn.Tests/Schemata.Flow.Bpmn.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Flow.Bpmn.Tests/Schemata.Flow.Bpmn.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CallActivity"`
  Run: `dotnet test tests/Schemata.Flow.Integration.Tests/Schemata.Flow.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CallActivityChildRuntimeShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Flow.Skeleton/Entities/SchemataProcessToken.cs src/Schemata.Flow.Bpmn tests/Schemata.Flow.Bpmn.Tests tests/Schemata.Flow.Integration.Tests/CallActivityChildRuntimeShould.cs`
  `git commit -m "fix(Flow): bind call completion to the current activation"`

### Task 28: ViewField lookahead 跳过注释

**Issues:** #291  
**Dependencies:** 无  
**Files:**
- Modify: `generators/Schemata.Modeling.Generator/Parser.cs:357-411`
- Test: `tests/Schemata.Modeling.Generator.Tests/ViewParserShould.cs:48-135`

**Interfaces:**
- Lookahead skips `Note` tokens before deciding `[` (options), `{` (body), `=` (assignment) and typed/untyped classification.
- Comments do not change the emitted `ViewField` type/nullable/name.

- [ ] **Step 1: 注释感知 lookahead**
  在 typed/untyped 分支前消费/跳过 `Note` token；保留 notes 到 `ViewField.Notes`，不改变后续 `[`/`{`/`=` 判定。
- [ ] **Step 2: 写解析测试**
  增加 `ViewField_WithNoteBeforeOptions_RemainsTyped`、`ViewField_WithNoteBeforeBody_RemainsUntyped`、`ViewField_WithNoteBeforeAssignment_RemainsTyped`；断言 type/nullable/name/notes 与无注释版本一致。
- [ ] **Step 3: 单次验证**
  Run: `dotnet build tests/Schemata.Modeling.Generator.Tests/Schemata.Modeling.Generator.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Modeling.Generator.Tests/Schemata.Modeling.Generator.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ViewParserShould"`
  Expected: PASS。
- [ ] **Step 4: Commit**
  `git add generators/Schemata.Modeling.Generator/Parser.cs tests/Schemata.Modeling.Generator.Tests/ViewParserShould.cs`
  `git commit -m "fix(Modeling): skip comments in view field lookahead"`

---

## Wave D：SCIM 最小合规

### Task 29: 共享 SCIM 协议与 Discovery

**Issues:** #123、#125、#127  
**Dependencies:** 无  
**Files:**
- Create: `src/Schemata.Scim/Schemata.Scim.csproj`
- Create: `src/Schemata.Scim/Constants/ScimConstants.cs`
- Create: `src/Schemata.Scim/Models/ScimResource.cs`
- Create: `src/Schemata.Scim/Models/ListResponse.cs`
- Create: `src/Schemata.Scim/Models/ServiceProviderConfig.cs`
- Create: `src/Schemata.Scim/Errors/ScimError.cs`
- Create: `src/Schemata.Scim/Controllers/ScimDiscoveryController.cs`
- Create: `src/Schemata.Scim/Extensions/ScimEndpointExtensions.cs`
- Modify: `Schemata.slnx`
- Test: `tests/Schemata.Scim.Tests/Schemata.Scim.Tests.csproj`
- Test: `tests/Schemata.Scim.Tests/ScimDiscoveryShould.cs`

**Interfaces:**
```csharp
public static class ScimConstants
{
    public const string BasePath = "/scim/v2";
    public const string MediaType = "application/scim+json";
    public const string ListResponseSchema = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    public const string ErrorSchema = "urn:ietf:params:scim:api:messages:2.0:Error";
}
```
- Discovery returns RFC 7643 §5/§6/§7 shapes; `ServiceProviderConfig` declares `patch/filter/sort/etag/bulk` false.
- SCIM errors use `schemas`/`status`/`scimType`/`detail`, not AIP-193.

- [ ] **Step 1: 建包与模型**
  创建 `Schemata.Scim`（依赖 Abstractions/Common/Transport.Http；不依赖 Identity/Authorization），加入 solution。实现 `ScimResource`/`ListResponse`/`ServiceProviderConfig`/`ScimError` 与 schema registry。
- [ ] **Step 2: Discovery**
  `ScimDiscoveryController` 映射 `GET /scim/v2/ServiceProviderConfig`、`/ResourceTypes`、`/Schemas` 及单项；返回 `application/scim+json`。
- [ ] **Step 3: 错误映射**
  SCIM endpoint 使用 `ScimError`；`ServiceProviderConfig` 的 optional 能力全部 false。不要复用 AIP-193 JSON。
- [ ] **Step 4: 写 discovery 测试**
  `ScimDiscoveryShould` 断言三个 endpoint 的 schema/id/meta、`schemas`、`totalResults`（适用时）、capability flags；不支持的 optional 能力明确为 false。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Scim.Tests/Schemata.Scim.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Scim.Tests/Schemata.Scim.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ScimDiscoveryShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Scim tests/Schemata.Scim.Tests Schemata.slnx`
  `git commit -m "feat(Scim): add shared protocol and discovery"`

### Task 30: Identity SCIM Users

**Issues:** #123、#124、#127  
**Dependencies:** Task 12、Task 29  
**Files:**
- Create: `src/Schemata.Identity.Scim/Schemata.Identity.Scim.csproj`
- Create: `src/Schemata.Identity.Scim/Controllers/ScimUsersController.cs`
- Create: `src/Schemata.Identity.Scim/Services/ScimUserAdapter.cs`
- Create: `src/Schemata.Identity.Scim/Features/SchemataIdentityScimFeature.cs`
- Modify: `Schemata.slnx`
- Test: `tests/Schemata.Identity.Scim.Integration.Tests/Schemata.Identity.Scim.Integration.Tests.csproj`
- Test: `tests/Schemata.Identity.Scim.Integration.Tests/ScimUsersShould.cs`

**Interfaces:**
- `ScimUserAdapter<TUser>` maps `SchemataUser`/custom `TUser`; `userName` non-empty, provider-wide unique, case-insensitive; `active` maps enabled state.
- CRUD uses Identity admission/uniqueness/confirmation/security-field rules and repository mutation; no provisioning principal.

- [ ] **Step 1: 建 adapter 包**
  创建 `Schemata.Identity.Scim`（依赖 `Schemata.Scim`、Identity.Foundation/Skeleton）；注册 feature 与 endpoints。
- [ ] **Step 2: Users CRUD**
  实现 POST/GET single/GET list/PUT/DELETE；PUT 只替换不创建；DELETE 204 后单项 404 且列表消失。`userName` 规则与 Identity manager 一致。
- [ ] **Step 3: Attribute mutability**
  POST/PUT 忽略 `readOnly`；`readWrite`/`immutable`/`writeOnly` 按 RFC 7643 §2.2/§7。错误使用 SCIM error shape。
- [ ] **Step 4: 写 Users 集成测试**
  `ScimUsersShould` 覆盖 create/get/list/replace/delete、`totalResults`/`startIndex`/`itemsPerPage`、`startIndex<1` 按 1、负 `count` 按 0、数量不超过 `count`、readOnly/immutable/writeOnly 正反例、userName 唯一/大小写、权限拒绝。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Identity.Scim.Integration.Tests/Schemata.Identity.Scim.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Identity.Scim.Integration.Tests/Schemata.Identity.Scim.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ScimUsersShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Identity.Scim tests/Schemata.Identity.Scim.Integration.Tests Schemata.slnx`
  `git commit -m "feat(Identity.Scim): add SCIM users adapter"`

### Task 31: Identity SCIM Groups

**Issues:** #124、#127  
**Dependencies:** Task 30  
**Files:**
- Create: `src/Schemata.Identity.Scim/Controllers/ScimGroupsController.cs`
- Create: `src/Schemata.Identity.Scim/Services/ScimGroupAdapter.cs`
- Modify: `src/Schemata.Identity.Scim/Features/SchemataIdentityScimFeature.cs`
- Test: `tests/Schemata.Identity.Scim.Integration.Tests/ScimGroupsShould.cs`

**Interfaces:**
- `displayName` required; members use SCIM id and `$ref` location; PUT replaces membership in one role/membership transaction.
- SCIM cannot grant unauthorized roles; canonical-name associations remain strings.

- [ ] **Step 1: Groups CRUD**
  实现 POST/GET single/GET list/PUT/DELETE；Group 映射 role + membership；PUT 在一个事务内替换成员集合。
- [ ] **Step 2: 权限边界**
  通过现有 Identity policy/admission 检查角色可授予性；禁止通过 SCIM 提权到未授权角色；不暴露 client secret/内部 tenant id。
- [ ] **Step 3: 写 Groups 集成测试**
  `ScimGroupsShould` 覆盖 displayName 必填、members `value`/`$ref`、PUT 替换/清空成员、事务回滚、权限拒绝、删除后列表消失。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Identity.Scim.Integration.Tests/Schemata.Identity.Scim.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Identity.Scim.Integration.Tests/Schemata.Identity.Scim.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ScimGroupsShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Identity.Scim tests/Schemata.Identity.Scim.Integration.Tests`
  `git commit -m "feat(Identity.Scim): add SCIM groups adapter"`

### Task 32: SCIM 证据与 surface matrix

**Issues:** #127、#128  
**Dependencies:** Task 29、Task 30、Task 31  
**Files:**
- Create: `docs/documents/scim.md`
- Create: `docs/documents/scim-surface-matrix.md`
- Test: `tests/Schemata.Identity.Scim.Integration.Tests/ScimSurfaceShould.cs`
- Test: `tests/Schemata.Scim.Tests/ScimErrorShould.cs`

**Interfaces:**
- Documented subset: discovery, Users/Groups POST/GET/PUT/DELETE, ListResponse/pagination, error shape, attribute mutability.
- Explicitly unsupported: PATCH, filter, sort, ETag/If-Match, Bulk, `/Me`, `.search`, attributes/excludedAttributes, Applications schema.
- SCIM is HTTP-only; gRPC no equivalent.

- [ ] **Step 1: 证据文档**
  写 `scim.md` 与 `scim-surface-matrix.md`，逐项标注 RFC 7643/7644 要求、实现状态、测试证据；不声称未实现能力。
- [ ] **Step 2: Surface matrix 测试**
  `ScimSurfaceShould` 按 per-operation 检查 HTTP 行为与 discovery 声明；`ScimErrorShould` 检查 `schemas`/`status`/`scimType`/`detail` 正反例。
- [ ] **Step 3: 单次验证**
  Run: `dotnet build tests/Schemata.Identity.Scim.Integration.Tests/Schemata.Identity.Scim.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Identity.Scim.Integration.Tests/Schemata.Identity.Scim.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ScimSurfaceShould"`
  Run: `dotnet test tests/Schemata.Scim.Tests/Schemata.Scim.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ScimErrorShould"`
  Expected: PASS。
- [ ] **Step 4: Commit**
  `git add docs/documents/scim.md docs/documents/scim-surface-matrix.md tests/Schemata.Identity.Scim.Integration.Tests/ScimSurfaceShould.cs tests/Schemata.Scim.Tests/ScimErrorShould.cs`
  `git commit -m "docs(Scim): record supported surface and evidence"`

---

## Wave E：RFC 8288 Web Linking

### Task 33: WebLink 模型与 parser/serializer

**Issues:** #196  
**Dependencies:** 无  
**Files:**
- Create: `src/Schemata.Transport.Http/Linking/WebLink.cs`
- Create: `src/Schemata.Transport.Http/Linking/LinkHeader.cs`
- Create: `src/Schemata.Transport.Http/Linking/LinkHeaderParser.cs`
- Create: `src/Schemata.Transport.Http/Linking/ILinkContributor.cs`
- Create: `src/Schemata.Transport.Http/Linking/LinkContext.cs`
- Modify: `src/Schemata.Transport.Http/Extensions/ServiceCollectionExtensions.cs`
- Test: `tests/Schemata.Transport.Http.Tests/LinkHeaderShould.cs`

**Interfaces:**
```csharp
public sealed record WebLink(
    string Target,
    string Rel,
    string? Anchor = null,
    string? Hreflang = null,
    string? Media = null,
    string? Title = null,
    string? TitleStar = null,
    string? Type = null,
    IReadOnlyDictionary<string, string>? Extensions = null);

public interface ILinkContributor
{
    IEnumerable<WebLink> GetLinks(LinkContext context);
}

public sealed record LinkContext(
    HttpContext HttpContext,
    object? Result,
    ResourceRegistration? Resource);
```
- Parser/serializer implement RFC 8288 §3 ABNF, quoted-string escapes, comma/semicolon, multiple link-values, RFC 3986 §5 relative target resolution, anchor context override.

- [ ] **Step 1: 模型与 parser**
  实现 `WebLink`/`LinkHeader`/`LinkHeaderParser`；target 使用 `Uri`/`Uri.TryCreate` 解析相对 URI，anchor 覆盖 context。扩展参数保持大小写语义。
- [ ] **Step 2: 序列化**
  `LinkHeader.Serialize(IEnumerable<WebLink>)` 输出单个 header；quoted-string 转义 `"`/`\`；不重复输出同一 link。
- [ ] **Step 3: DI seam**
  `ILinkContributor` 注册为 enumerable；`LinkContext` 只携带显式请求/结果，不使用静态状态。
- [ ] **Step 4: 写 parser 测试**
  覆盖 quoted/多重 link、相对 target、anchor、扩展参数、逗号/分号、非法输入；round-trip 等价；恶意输入不抛出未处理异常。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Transport.Http.Tests/Schemata.Transport.Http.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Transport.Http.Tests/Schemata.Transport.Http.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LinkHeaderShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Transport.Http/Linking src/Schemata.Transport.Http/Extensions/ServiceCollectionExtensions.cs tests/Schemata.Transport.Http.Tests/LinkHeaderShould.cs`
  `git commit -m "feat(Transport.Http): add RFC 8288 link model and parser"`

### Task 34: HTTP 响应 shaping 与 Resource/Scheduling/Push/Flow links

**Issues:** #197、#198、#200、#201  
**Dependencies:** Task 33  
**Files:**
- Create: `src/Schemata.Transport.Http/Linking/LinkHeaderResultFilter.cs`
- Modify: `src/Schemata.Transport.Http/Extensions/ServiceCollectionExtensions.cs`
- Create: `src/Schemata.Resource.Http/Linking/ResourceLinkContributor.cs`
- Create: `src/Schemata.Scheduling.Http/Linking/SchedulingLinkContributor.cs`
- Create: `src/Schemata.Push.Http/Linking/PushLinkContributor.cs`
- Create: `src/Schemata.Flow.Http/Linking/FlowLinkContributor.cs`
- Test: `tests/Schemata.Resource.Http.Integration.Tests/LinkHeaderShould.cs`
- Test: `tests/Schemata.Scheduling.Integration.Tests/LinkHeaderShould.cs`
- Test: `tests/Schemata.Push.Http.Integration.Tests/LinkHeaderShould.cs`
- Test: `tests/Schemata.Flow.Integration.Tests/LinkHeaderShould.cs`

**Interfaces:**
- One `IAlwaysRunResultFilter` serializes links after the MVC result; components register `ILinkContributor` only.
- Resource: self/canonical/next/prev/first/last/related child collections.
- Scheduling: job/execution/owner relations.
- Push: subscription list self/provider-filtered variants.
- Flow: process/token/definition/catch relations.

- [ ] **Step 1: 统一 shaping**
  `LinkHeaderResultFilter` 收集 `IEnumerable<ILinkContributor>`，调用 `GetLinks`，用 `LinkHeader.Serialize` 写入 `Link` header；无 link 时不写 header。组件不各自拼 header。
- [ ] **Step 2: Resource contributor**
  从 Resource result/registration 生成 self/canonical/分页 first/prev/next/last 与 child collections；分页使用现有 page token，不引入第二套状态。
- [ ] **Step 3: Scheduling/Push/Flow contributors**
  生成 spec 表列出的关系；不包含 secret/token value/内部 tenant id。
- [ ] **Step 4: 写 HTTP 集成测试**
  四个 `LinkHeaderShould` 使用 parser 解析真实响应 header，断言 rel/target；相对 target 解析到请求 URI；无 link 时 header 不存在；gRPC 不产生 Link。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Resource.Http.Integration.Tests/Schemata.Resource.Http.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Resource.Http.Integration.Tests/Schemata.Resource.Http.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LinkHeaderShould"`
  Run: `dotnet test tests/Schemata.Flow.Integration.Tests/Schemata.Flow.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LinkHeaderShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Transport.Http/Linking src/Schemata.Resource.Http/Linking src/Schemata.Scheduling.Http/Linking src/Schemata.Push.Http/Linking src/Schemata.Flow.Http/Linking tests/*/LinkHeaderShould.cs`
  `git commit -m "feat(Http): emit RFC 8288 links for resource surfaces"`

### Task 35: Authorization/Identity/Insight/Report links 与边界

**Issues:** #202、#203、#204、#205、#199  
**Dependencies:** Task 34  
**Files:**
- Create: `src/Schemata.Authorization.Http/Linking/AuthorizationLinkContributor.cs`
- Create: `src/Schemata.Identity.Http/Linking/IdentityLinkContributor.cs`
- Create: `src/Schemata.Insight.Http/Linking/InsightLinkContributor.cs`
- Create: `src/Schemata.Report.Http/Linking/ReportLinkContributor.cs`
- Test: `tests/Schemata.Authorization.Integration.Tests/LinkHeaderShould.cs`
- Test: `tests/Schemata.Identity.Integration.Tests/LinkHeaderShould.cs`
- Test: `tests/Schemata.Insight.Http.Integration.Tests/LinkHeaderShould.cs`
- Test: `tests/Schemata.Report.Integration.Tests/LinkHeaderShould.cs`

**Interfaces:**
- Authorization: Application/Scope/Token management resources; no secret.
- Identity: User/Role; no session resource if none exists.
- Insight: query result self/next/prev using `InsightPageToken`.
- Report: definition/snapshot/chunk/schedule relations.
- Event: no new links (#199 Won't Fix).

- [ ] **Step 1: 四个 contributor**
  按 spec 表实现 link 范围；只读 result/registration，不查询额外 secret。
- [ ] **Step 2: 边界**
  Link 只用于 HTTP；gRPC 不声明 RFC 8288 等价。相对 target 使用请求 URI；分页复用现有 token。
- [ ] **Step 3: 写集成测试**
  四个 `LinkHeaderShould` 解析真实响应，断言 rel/target、无 secret/tenant id、无重复序列化；Identity 不发明 session link。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LinkHeaderShould"`
  Run: `dotnet test tests/Schemata.Insight.Http.Integration.Tests/Schemata.Insight.Http.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LinkHeaderShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Authorization.Http/Linking src/Schemata.Identity.Http/Linking src/Schemata.Insight.Http/Linking src/Schemata.Report.Http/Linking tests/*/LinkHeaderShould.cs`
  `git commit -m "feat(Http): add management and report links"`

---

## Wave F：清理与 AdviceContext 收口

### Task 36: 包与表面清理

**Issues:** #343  
**Dependencies:** 无  
**Files:**
- Delete: `src/Schemata.Caching.Distributed/`
- Delete: `src/Schemata.Entity.Owner/`
- Modify: `src/Schemata.Tenancy.Skeleton/Schemata.Tenancy.Skeleton.csproj:18`
- Modify: `AGENTS.md:5,30,40`
- Modify: `src/Schemata.Authorization.Skeleton/AuthorizationConstants.cs`
- Modify: `src/Schemata.Authorization.Foundation/Services/RegistrationMetadataMapper.cs`
- Modify: `src/Schemata.Mapping.Skeleton/ISimpleMapper.cs`（如 LSP 发现重复表面）
- Test: `tests/Schemata.Common.Tests/`、`tests/Schemata.Mapping.Tests/`

**Interfaces:**
- No residual package directories; `Schemata.Tenancy.Skeleton` has no unused `Entity.Repository` reference.
- `RegistrationMetadataMapper.ResponseTypeRequirements` lives in `AuthorizationConstants` (Task 26 already moves it); no duplicate table.
- `ISimpleMapper` has one surface and two provider implementations.

- [ ] **Step 1: 删除残留**
  删除 `src/Schemata.Caching.Distributed`、`src/Schemata.Entity.Owner`；确认 `Schemata.slnx` 无引用。
- [ ] **Step 2: 清理引用/计数**
  移除 `Schemata.Tenancy.Skeleton` 未使用的 `Entity.Repository` 引用；按最终 `Schemata.slnx` 数量修正 `AGENTS.md`：当前 88 src / 60 test，本计划新增两个 SCIM 包及其测试后为 90 src / 62 test。
- [ ] **Step 3: 收敛常量/mapper**
  确认 response_type 表只在 `AuthorizationConstants`；用 LSP references 检查 `ISimpleMapper`，若只有单一接口则记录 no-op，不制造第二套表面。
- [ ] **Step 4: 单次验证**
  Run: `dotnet build Schemata.slnx -c Release`
  Run: `dotnet test tests/Schemata.Mapping.Tests/Schemata.Mapping.Tests.csproj -c Release --no-build`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Caching.Distributed src/Schemata.Entity.Owner src/Schemata.Tenancy.Skeleton/Schemata.Tenancy.Skeleton.csproj AGENTS.md`
  `git commit -m "chore: remove residual packages and stale references"`

### Task 37: 移除 AdviceContext ambient 与残余 bag

**Issues:** #339  
**Dependencies:** Task 20、Task 23  
**Files:**
- Modify: `src/Schemata.Abstractions/Advisors/AdviceContext.cs`
- Modify: `src/Schemata.Messaging.Skeleton/IRequestHandler.cs`
- Modify: `src/Schemata.Messaging.Skeleton/Runtime/InProcessRequestDispatcher.cs:48-49`
- Modify: `src/Schemata.Messaging.Skeleton/Runtime/InProcessStreamDispatcher.cs:68-69,118-119`
- Modify: `src/Schemata.Actor.Foundation/Runtime/ActorInstance.cs:427-429,578-580,648-650`
- Modify: `src/Schemata.Event.Foundation/Runtime/InProcessEventBus.cs:98-99,133-134`
- Modify: `src/Schemata.Event.RabbitMq/Runtime/RabbitMqConsumerHost.cs:208-209`
- Modify: `src/Schemata.Event.RabbitMq/Runtime/RabbitMqEventBus.cs:113-114`
- Modify: `src/Schemata.Scheduling.Foundation/JobExecutionDispatcher.cs:203-204`
- Modify: `src/Schemata.*/**/Handlers/*.cs`（所有 `AdviceContext.Require()` 调用）
- Modify: `src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs:60-61`
- Modify: `src/Schemata.Flow.Foundation/Handlers/FlowHandlerSupport.cs:231,751`
- Modify: `src/Schemata.Insight.Foundation/Execution/PlanExecutor.cs:376`
- Test: `tests/Schemata.Abstractions.Tests/Advisors/AdviceContextAmbientShould.cs`
- Test: `tests/Schemata.Messaging.Skeleton.Tests/RequestDispatcherShould.cs`
- Test: `tests/Schemata.Actor.Foundation.Tests/AmbientTurnShould.cs`
- Test: `tests/Schemata.Scheduling.Tests/JobExecutionAmbientContextShould.cs`

**Interfaces:**
```csharp
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(
        TRequest request,
        AdviceContext context,
        CancellationToken ct = default);
}

public class AdviceContext
{
    public AdviceContext(IServiceProvider sp);
    public IServiceProvider ServiceProvider { get; }
}
```
- Delete `Current`/`Require`/`Establish`/`Set`/`TryGet`/`Get`/`Has`/`Use` and all ambient/marker call sites.
- Pipeline roots construct `AdviceContext` explicitly and pass it to dispatcher/handler; handlers can receive an explicit context in tests without a dispatcher scope.

- [ ] **Step 1: 改变 handler contract**
  `IRequestHandler.HandleAsync` 增加 `AdviceContext context`；dispatcher 按请求构造 context 并传入。更新所有 handler 实现和 keyed handler 调用。
- [ ] **Step 2: 迁移 pipeline roots**
  Actor turn、event publish/consume、RabbitMQ consumer、scheduling job fire、stream dispatcher 显式构造并传递 context；删除 `Establish`/`Current` fallback。
- [ ] **Step 3: 删除 ambient/bag**
  `AdviceContext` 只保留 `ServiceProvider`；删除静态 `AsyncLocal`、`Require`、`Establish` 和 typed bag。Advisor 仍通过 `AdviseAsync(ctx, ...)` 显式接收。
- [ ] **Step 4: 迁移测试**
  删除 `AdviceContextAmbientShould` 的 ambient 断言；所有 handler 测试改为显式传入 context；增加嵌套/并行 handler 测试，断言 context 不串。
- [ ] **Step 5: 单次验证**
  Run: `dotnet build tests/Schemata.Messaging.Skeleton.Tests/Schemata.Messaging.Skeleton.Tests.csproj -c Release`
  Run: `dotnet test tests/Schemata.Messaging.Skeleton.Tests/Schemata.Messaging.Skeleton.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RequestDispatcherShould"`
  Run: `dotnet test tests/Schemata.Scheduling.Tests/Schemata.Scheduling.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~JobExecutionAmbientContextShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Abstractions/Advisors/AdviceContext.cs src/Schemata.Messaging.Skeleton src/Schemata.Actor.Foundation src/Schemata.Event.Foundation src/Schemata.Event.RabbitMq src/Schemata.Scheduling.Foundation src/Schemata.Authorization.Foundation src/Schemata.Flow.Foundation src/Schemata.Insight.Foundation tests`
  `git commit -m "refactor!: remove ambient advice contexts"`

---

## 规范覆盖

| Spec 工作项 | Task |
|---|---|
| A1 #335 | 1 |
| A2 #334 | 2 |
| A3 #345 | 3 |
| A4 #337 | 4 |
| A5 #342 | 5 |
| A6 #348 | 6 |
| A7 #296 | 7 |
| G 实体矩阵 | 8–15 |
| B1 #349 repository 两模式 | 17 |
| B2 #350 移除默认 `ITenant` | 16 |
| B3 #349 scheduling tenant-agnostic | 18 |
| B4 cache/state | 21 |
| B5 #338 composition hack | 19 |
| B6 #336 suppression marker | 20 |
| B7 tenant 边界审计 | 21 |
| C1 #346 | 22 |
| C2 #347 | 23 |
| C3 #340 | 24 |
| C4 #341 | 25 |
| C5 #344 | 26 |
| C6 #285 | 27 |
| C7 #291 | 28 |
| D1 共享 SCIM 包 | 29 |
| D2/D3 Users/Groups | 30、31 |
| D4 证据/surface matrix | 32 |
| E1 RFC 8288 模型 | 33 |
| E2 组件接入（Resource/Scheduling/Push/Flow） | 34 |
| E3 其余组件与边界 | 35 |
| F1 #343 清理 | 36 |
| F2 #339 AdviceContext | 37 |

## 依赖与波次

1. **Wave A（P0）**：Task 1–7。Task 1/3/4 可并行；Task 6/7 独立。Task 5 与 Task 6 都触及 Tenancy state，由同一集成 owner 合并。
2. **Wave G（实体）**：Task 8–15。Task 8 先于 Task 9；Task 9 先于 Task 10；Task 11 先于 Task 12/13；Task 12 先于 Task 14；Task 15 依赖 Task 9。G 任务可在不同 component worktree 并行，但 `Schemata.slnx` 由集成 owner 统一修改。
3. **Wave B（Tenancy）**：Task 16 依赖 G8/G9/G10；Task 20 独立并先于 Task 17；Task 17 依赖 16/20；Task 18 依赖 9/16；Task 19 依赖 8/17；Task 21 在 16–20 后。
4. **Wave C（一致性）**：Task 22–28 相互独立；Task 27 依赖 Task 10；Task 25/28 触及 generator，由同一 owner 串行。
5. **Wave D（SCIM）**：Task 29 先于 30/31；Task 30 依赖 Task 12；Task 32 依赖 29–31。
6. **Wave E（Link）**：Task 33 先于 34/35；Task 34/35 可并行。
7. **Wave F（清理）**：Task 36 独立；Task 37 依赖 Task 20/23。最终集中验收在所有任务后执行。

## 集中验收

- [ ] **Build once:** `dotnet build Schemata.slnx -c Release --no-incremental -p:TreatWarningsAsErrors=true`
- [ ] **Test once per project group:** `dotnet test tests/<project>/<project>.csproj -c Release --no-build`；不重复同一测试集合，不连续强制 rebuild。
- [ ] **P0 证据:** token veto 双 provider、probe.log 只读内容根、scheduling staging 失败、provider cache 并发/取消、SessionCache 慢后端、auth 并发、JsonDocument 异常路径。
- [ ] **Tenancy 证据:** 默认实体无 `ITenant`；自定义 `ITenant` 类型经 feature 注册后隔离；分库不要求 `ITenant`；scheduling 无 adapter/tenant 探测；per-tenant 调度由用户 tenant 注册提供；无 `Tenancy.Foundation → Scheduling`；`TenantContext`/`TenantIdentity`/`TenantResolveException` 位于 `Tenancy.Skeleton`；非 Tenancy component 无 tenant 探测或 scope 选择。
- [ ] **实体证据:** G2 每一行至少一个自定义子类型完成 `Use*` 注册、repository/EF/LinqToDB 映射和主要操作；默认内置类型行为不变；component 内无硬编码内置实体；`Tenancy.Actor` 使用自定义 Actor 类型持久化。
- [ ] **SCIM 证据:** discovery、Users/Groups POST/GET/PUT/DELETE、ListResponse/pagination、SCIM error shape、attribute mutability 正反例；`ServiceProviderConfig` 如实声明 optional 能力；权限拒绝；#127/#128 文档。
- [ ] **RFC 8288 证据:** parser/serializer round-trip；quoted/多重 link/相对 target/anchor；各组件 HTTP header 可被参考 parser 解析；无重复序列化；无 secret/tenant id。
- [ ] **回归:** AIP standard/custom methods、canonical-name refs、OAuth/OIDC 现有测试保持。
- [ ] **Review:** 完整 diff 对照本计划；重点检查 commit/cache 边界、无 ambient context、caller deny、tenant 隔离、NoWrite、两个 provider 语义。

## 执行交接

本计划只定义实施步骤，不修改产品代码。建议 **subagent-driven**：每个 Task 一个实现 owner，独立 reviewer gate；Wave 内独立任务并行，共享 `Schemata.slnx`/`Directory.Build.props`/shared contracts 由集成 owner 统一修改。用户审阅并选择执行方式后才进入实施；未批准前不创建 feature worktree、不改 source、不关闭 issues。
