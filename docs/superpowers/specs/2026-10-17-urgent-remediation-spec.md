# Schemata 紧急修复与协议面规范

## 基线与范围

- 基线：HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`。
- 本规范定义待实施的设计，不修改产品代码；实施阶段必须重新读取当前源文件。
- 规范覆盖 P0 安全与持久化、Tenancy 边界、通用实体替换、SCIM 最小合规集、RFC 8288 Web Linking，以及一致性清理。
- 工作项对应 Gitea `cyp/Schemata` issues `#123–#128`、`#196–#205`、`#285`、`#291`、`#296`、`#334–#350`。

## 目标

1. 消除安全、持久化一致性和请求生命周期上的 P0 缺陷。
2. 收敛 Tenancy：租户逻辑只存在于 `Schemata.Tenancy.*`；其他 component 不探测 tenant、不选择 tenant scope、不读取 `TenantContext`。
3. 所有含数据库实体的 component 在 `Use*` 阶段接受自定义实体类型；用户类型以子类型约束满足字段与接口需求。
4. 落地 SCIM 最小合规集（RFC 7644/7643 强制功能）与 RFC 8288 Web Linking 协议面。
5. 保持既有 AIP、OAuth 2.0/OIDC、canonical-name 关联、`RelaxXXX` 降级选项的契约。

## 非目标

- CIBA、BFF、Quota 持久额度、Rules、Orleans 跨节点、ID Token JWE、主动密钥轮换、step-up、历史 password grant。
- SCIM 首期只覆盖 RFC 7644/7643 的强制功能；PATCH、filter、sort、ETag、Bulk、`/Me`、`.search`、attributes/excludedAttributes、Applications 自定义 schema 属于后续扩展阶段。
- AIP/CEL 语言语义与编译器结构保持现状；只修复已确认的边界缺陷。
- 应用业务实体的租户模型由框架用户负责；Tenancy 不提供业务域隔离机制。
- 迁移工具、兼容 alias、双写、旧数据读取、startup guard、遥测。

## 工作项

| 优先级 | Issue | 目标 | 工作流 |
|---|---|---|---|
| P0 | #334 | 删除 cookie 注册中的 `probe.log` 写入 | A |
| P0 | #335 | token publication/slot 写入强制 `MutationResult.Applied` | A |
| P0 | #345 | Scheduling 终态与 job 状态在一个可重试边界内结算 | A |
| P0 | #337 | 移除 provider cache 的 `[ThreadStatic]` 与同步等待 | A |
| P0 | #342 | 移除 `SessionCache` 请求路径上的 sync-over-async | A |
| P0 | #348 | 消除 `ScopedAuthenticationService` 的 `RequestServices` 改写 | A |
| P0 | #296 | 释放 `AuthorizationDetailsService.Parse` 的 `JsonDocument` | A |
| P1 | #349 | Tenancy 自动注入与基础设施隔离收敛 | B |
| P1 | #350 | 移除默认 `ITenant` 实体，feature 接受自定义实体类型 | B |
| P1 | 通用实体替换 | 所有含数据库实体的 component 在 `Use*` 暴露泛型实体 | G |
| P1 | #338 | 移除 Tenancy 组合期的 concrete-type/descriptor/probe hack | B |
| P1 | #336 | 用显式操作参数替代 suppression marker | B |
| P1 | #346 | 用 typed registry 替代 DI 中的 `ConcurrentDictionary` | C |
| P1 | #347 | 统一 `HandledResponse<T>` 与执行 payload 的 handled output | C |
| P1 | #340 | 注册期拒绝重复 canonical pattern/collection | C |
| P1 | #341 | 由现有 generator 生成 AdviceRunner arity | C |
| P1 | #344 | 完成 OIDC DCR response_type 表 | C |
| P1 | #285 | CallActivity 按当前 activation 匹配 child completion | C |
| P1 | #291 | ViewField lookahead 跳过注释 | C |
| P2 | #123–#125, #127–#128 | SCIM 最小合规：共享协议、Users、Groups、证据、surface matrix | D |
| P3 | #126 | SCIM Applications 自定义 schema 扩展 | D |
| P2 | #196–#205 | RFC 8288 共享 Link 模型与各组件 links；#199 保持 Won't Fix | E |
| P3 | #343 | 陈旧目录、未使用引用、常量与 mapper 表面清理 | F |
| P3 | #339 | 移除 `AdviceContext` 静态 ambient 与残余 untyped bag | F |

## 全局约束

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

## 工作流 A：P0 安全与持久化

### A1 #335 Token store 强制写入

`RepositoryTokenStore.CreateAsync`、`UpdateAsync`、`SetAsync`、`GetOrCreateCoreAsync` 对每个必需 `AddAsync`/`UpdateAsync` 检查 `MutationResult.Applied`。未应用时抛出现有 `SchemataResourceErrors.PreconditionFailed<SchemataToken>`，不提交空事务。

`GetOrCreateCoreAsync` 在 lost `TryAdd` 后仍无法读到 winner 时 fail closed，返回 `AbortedException`。`CreateAsync`/`UpdateAsync` 的调用者语义保持不变。

验收：EF Core 与 LinqToDB 各覆盖 add veto、update veto、create 失败、get-or-create winner 消失；断言存储行与返回失败。

### A2 #334 删除 probe.log

删除 `TenantCookieRegistration.cs:59` 的 `File.AppendAllText`。需要诊断时使用现有 `ILogger` 的 Debug 级日志，不在 options 构建中写工作目录。

验收：只读内容根下构建 tenant cookie options 不触发文件写入。

### A3 #345 Scheduling 终态结算

`JobExecutionDispatcher.FinalizeAsync` 不再先提交 execution 终态、再通过公共 `IRequestDispatcher` 提交 job 状态。一个 scheduling-owned 结算入口在同一 UoW 内完成 execution settle 与 job state/timer 决策；provider 不支持跨 store 事务时，先持久化可重试的 pending stage，再 settle execution。

`StageJobExecutionResultRequest` 不再作为公共 handler 暴露；内部结算通过类型化 owner 调用，避免 consumer request advisor 干预。job `NextRunTime`/`State` 计算只保留一个 owner；`DefaultStageJobExecutionResultHandler` 与 `DefaultScheduleJobHandler` 不重复 missed-fire 计算。

验收：staging 永久失败或 advisor veto 后，execution 终态与 job schedule 不丢失；one-time 不残留 Active/旧 NextRunTime；recurring 继续 re-arm。

### A4 #337 Provider cache 等待

`MemoryCacheTenantProviderCache` 移除 `[ThreadStatic]` 构建守卫；构建所有权记录在 per-key `Pending` 上。等待者使用可取消的异步 completion，不调用 `GetAwaiter().GetResult()`。`DisposeProvider` 使用可等待的异步释放路径，不在请求构造路径阻塞。

验收：同一 tenant 首请求并发、等待期间取消、构建异常、provider 退休释放；无线程池阻塞与 provider 泄漏。

### A5 #342 SessionCache

`SessionCache` 的异步成员直接等待 provider；同步成员只在有界同步 backend 上执行，不再对 Redis 异步 provider 执行 `GetAwaiter().GetResult()`。请求路径只调用异步成员。

验收：慢后端下请求线程不被无界阻塞；异步取消传播；session 读写与 tenant namespace 一致。

### A6 #348 Authentication scope 路由

`ScopedAuthenticationService` 通过 keyed/optional `IAuthenticationService` 显式接收目标 scope provider，不再改写 `HttpContext.RequestServices`。删除未使用的 `AuthenticationSchemeScope.ScopeKey`；需要注册身份时使用显式 keyed 注册。

验收：tenant-owned scheme 与 host scheme 在同一请求内可连续认证；并发认证操作不观察到错误 provider；challenge/sign-in/sign-out 语义保持。

### A7 #296 JsonDocument

`AuthorizationDetailsService.Parse` 用 `using var document = JsonDocument.Parse(raw)`；校验失败路径同样释放。返回的 `JsonArray` 已是独立克隆，不依赖 document 生命周期。

验收：正常与异常路径均无未释放 document；现有 descriptor 校验结果不变。

## 工作流 B：Tenancy 边界

### B1 Repository 两模式与 Tenancy 依赖

- 分库实例：`UseDatabase` 保持显式 per-entity source 配置，实体不需要 `ITenant`。
- Shared table：repository 隔离由 open-generic `AdviceAddTenant<>`、`AdviceBuildQueryTenant<>`、`AdviceUpdateTenant<>`、`AdviceRemoveTenant<>` 自动注入。非 `ITenant` 实体返回 `Continue`。
- 删除 `TenantRepositoryBuilder.UseSharedTable`、`UseRepositories` 的类型 harvest、`Guid.NewGuid()` 占位与 `IsRegistrationProbe` bool。Host/tenant binding 从 DI 解析。
- 删除 `Tenancy.Foundation` 对 `Schemata.Entity.Cache` 的依赖。tenant handler 使用 `Entity.Repository` 的显式 query suppression 参数，不把 handler 移入 `Tenancy.Caching`。
- `Tenancy.Foundation.csproj` 不再引用 `Schemata.Entity.Cache`、`Schemata.Scheduling.Skeleton`。

验收：用户注册自定义 `ITenant` 实体后无需额外 tenant builder 调用即可隔离；host 只见 host 行；跨租户 update/remove 明确失败；分库模式不要求 `ITenant`；`SchemataTenancyBuilder.cs:33-36` 与 `TenantRepositoryBuilder.cs:50` 的占位/harvest 分支消失。

### B2 移除默认 ITenant 实体

从下列默认实体移除 `ITenant` 实现、`TenantUid` 与 tenant-specific indexes：

- `Schemata.Actor.Skeleton/Entities/SchemataActor.cs:19-25`
- `Schemata.Flow.Skeleton/Entities/SchemataProcess.cs:15-26`
- `Schemata.Flow.Skeleton/Entities/SchemataProcessToken.cs:21-27`
- `Schemata.Flow.Skeleton/Entities/SchemataProcessTransition.cs:16-22`
- `Schemata.Flow.Skeleton/Entities/SchemataProcessCompensation.cs:14-17`
- `Schemata.Flow.Skeleton/Entities/SchemataProcessParticipant.cs:13-20`
- `Schemata.Flow.Skeleton/Entities/SchemataProcessSource.cs:16-23`
- `Schemata.Flow.Repository/SchemataFlowEffectIntent.cs:21-27`
- `Schemata.Scheduling.Skeleton/Entities/SchemataJob.cs:16-21`
- `Schemata.Scheduling.Skeleton/Entities/SchemataJobExecution.cs:25-28`

通用实体替换规则见 G；`UseActor`、`UseFlow`、`UseScheduling` 的泛型实体参数是 G 的一部分。框架默认实体保持无租户字段；shared-table 隔离只在用户自定义类型实现 `ITenant` 后生效。

验收：默认 feature 的实体不实现 `ITenant`；自定义类型经 feature 注册后由 repository advisor 隔离；`FlowTenantRepositoryShould` 改用自定义 Flow 子类型，不直接构造默认 `SchemataProcess*`；现有 AIP/OAuth/DTO 契约不因类型参数改变。

### B3 Scheduling 无 tenant 探测

`TenantJobExecutionAdapter<TTenant>` 当前是 Tenancy-owned 的 `IJobExecutionStore` 包装：host `JobExecutionDispatcher` 在没有 tenant context 时枚举 tenant catalog，创建 tenant DI scope、进入 `TenantContext`，读取每个 tenant 的 keyed store；执行行通过 `MessageContext` 恢复 tenant scope。它是“一个 host 调度器 + 每租户 store/scope”，不是每租户独立调度器。

删除以下内容：

- `Schemata.Tenancy.Foundation/Scheduling/TenantJobExecutionAdapter.cs:17-122`
- `Schemata.Tenancy.Foundation/Scheduling/TenantSchedulingExtensions.cs:16-36`
- `Schemata.Tenancy.Foundation/TenancyConstants.cs:10-12` 的 `Scheduling.Backend`
- `Schemata.Tenancy.Foundation.csproj:22` 的 `Schemata.Scheduling.Skeleton` 引用

Scheduling 保持 tenant-agnostic：不枚举 tenant、不创建 tenant scope、不读取 `TenantContext`、不启动 per-tenant dispatcher。host dispatcher 只处理 host 行。

需要 per-tenant 调度时，用户在 tenant 注册中提供该租户的 scheduling 服务与生命周期，例如在 `ForTenant`/`DynamicOverrides`/`UseRepositories` 的 tenant 注册中注册自己的 scheduler/dispatcher。框架不探测 tenant，也不为 tenant 启动 hosted service。

验收：Scheduling 源码无 tenant 探测/scope 选择/adapter；host 调度只读 host 行；用户 tenant 注册可独立提供 per-tenant 调度；无 `Tenancy.Foundation → Scheduling` 引用。

### B4 Cache 与 State

- Cache：`UseCache` 是显式 tenant 注册。tenant 容器内用 `TenantCacheKeyNormalizer` 包装所选 outlet；Tenancy 启用本身不自动包装 host `ICacheProvider`。
- State：cookie/bearer/session/antiforgery 的 scheme 声明保持显式；隔离由 tenant protector/cookie namespace 完成；不增加业务数据隔离。
- `SessionCache` 的同步阻塞问题由 A5 处理；Tenancy.Caching 不在其他 component 中注入 tenant cache 逻辑。

验收：只有显式 `UseCache` 的 tenant 获得 namespace；host `ICacheProvider` 不因启用 Tenancy 被自动包装；state scheme 仍由显式注册决定。

### B5 #338 组合 hack

- `TenantPolicyEvaluator` 不再用 `inner.GetType() == typeof(PolicyEvaluator)`；通过 keyed 注册 inner provider 构造，`SchemataTenancyFeature.cs:54-55,81` 不再扫描/替换服务描述符。
- `TenantAntiforgeryRegistration` 不扫描并删除 `IStatePipelineProvider` 描述符；状态分支由单一 provider 注册扩展开口贡献。
- `TenantMessagingExtensions` 不在容器构建期创建 probe scope（`TenantMessagingExtensions.cs:33-44`）；dispatcher aliasing 由注册期契约决定。
- `Tenancy.Actor` 只通过显式 `tenancy.UseActor()` 启用；通过 Actor 公开 factory/runtime seam 注册 `TenantActorSystem<TActor>`，不使用 `InternalsVisibleTo`，不替换不属于自己的 descriptor。

验收：无 `GetType()` 比较、无 tenant 容器构建期 probe、无 `IStatePipelineProvider` 描述符删除；tenant 注册仍解析到正确的 policy、controller、dispatcher 和 actor system。

### B6 #336 suppression marker

`IRepository.SuppressAddValidation`、`SuppressUpdateValidation`、`SuppressQuerySoftDelete`、`SuppressSoftDelete`、`SuppressTimestamp` 与 owner/cache suppression extensions 改为显式操作参数。删除 `AdviceContext.Use/Has` marker 分支，迁移 `FlowSourceReadScope`、`ProcessPersistence`、reference existence、Undelete/Delete、tenant resolution 等调用点。

验收：suppression 不通过 context 泄漏；嵌套调用与并行 repository 操作互不影响。

### B7 Tenant 边界审计

全 `src/` 符号审计、项目引用审计与 `InternalsVisibleTo` 审计结果：

| 发现 | 处理 |
|---|---|
| 非 Tenancy component 无运行时 `TenantContext`、`TenantUid`、tenant scope 读取 | 保持；新增代码不得引入 |
| 默认 Actor/Flow/Scheduling 实体实现 `ITenant` | B2 + G |
| Tenancy 为 Scheduling 提供 adapter、keyed backend 与 tenant 枚举 | B3 |
| `Tenancy.Foundation` 引用 `Entity.Cache`、`Scheduling.Skeleton` | B1/B3 |
| `TenantMessagingExtensions` 在 tenant 容器构建期探测 dispatcher | B5 |
| `TenantPolicyEvaluator` 具体类型比较、descriptor 扫描 | B5 |
| `UseRepositories` 的 harvest/占位 GUID/`IsRegistrationProbe` | B1 |
| `ActorStateStore` 硬编码 `SchemataActor`；`Tenancy.Actor` 依赖 Actor internals | G1/G2 |
| `TenantContext`/`TenantIdentity`/`TenantResolveException` 位于 `Schemata.Abstractions` | 移到 `Schemata.Tenancy.Skeleton`；`ITenant` trait 保留在 Abstractions |
| 非 Tenancy component 注释描述 tenant 注册 | 移到 Tenancy 文档/指南，组件注释只描述通用扩展入口 |
| `FlowTenantRepositoryShould` 直接使用默认 `ITenant` 实体 | B2/G3 改为自定义 Flow 子类型 |
| `Insight.TenantExecutionShould` 使用显式 `ForTenant` | 保持，作为 per-tenant 注册模板 |

边界验收：

- `Schemata.Abstractions` 不再含 `TenantContext`、`TenantIdentity`、`TenantResolveException`；只保留 `ITenant` 与通用错误常量。`Schemata.Tenancy.Skeleton` 补齐直接项目引用，承载这三个运行时类型。
- 非 Tenancy component 源码不出现 tenant 探测、tenant scope 创建或 `TenantContext` 读取。
- tenant 注册文档只存在于 Tenancy 文档/指南，不嵌入其他 component 的 XML 注释。更新 `docs/guides/multi-tenancy.md`、`docs/guides/flow.md`、`docs/guides/actor.md`、`docs/documents/scheduling/persistence.md` 中的旧 adapter/default `ITenant` 说明。
- tenant 测试使用显式 tenant 注册；默认实体测试不依赖 `ITenant`。

## 工作流 C：一致性与债务

### C1 #346 Active execution registry

把 `ConcurrentDictionary<string, CancellationTokenSource>` 服务替换为 typed `IActiveExecutionRegistry`，由 Scheduling 拥有。`JobExecutionDispatcher`、`ExecutionLease`、`DefaultOperationService` 通过同一接口注册、移除、取消；dispatcher 不再 `GetService(...) ?? new()`。

### C2 #347 Handled output

定义共享 `IHandledResponse<TResponse>` 契约，`HandledResponse<TResponse>` 作为实现；`ResourceMethodExecution<TEntity,TRequest,TResponse>`、`IdentityOperationExecution<TResponse>` 与标准 resource payload 使用同一 handled-output 契约。Identity 每个操作只保留一个 response carrier。

### C3 #340 Canonical pattern 歧义

`ResourceRegistry` 在注册期拒绝重复 normalized canonical pattern 与 collection name；`DefaultResourceTypeResolver` 不再 first-wins。typed/polymorphic `[ResourceReference]` 解析与 existence 校验得到确定目标。

### C4 #341 AdviceRunner arity

扩展现有 `Schemata.Advice.Generator` 生成 `AdviceRunner` arity 类；16 份手写实现删除。生成器输出与现有 `Advisor.For` 调用签名一致。

### C5 #344 OIDC DCR 表

`RegistrationMetadataMapper` 的 response_type→grant_types 表补齐 OIDC Registration §2 六项；常量放入 `AuthorizationConstants`。DCR 与 discovery/authorize 支持的 response type 保持一致。

### C6 #285 CallActivity activation

`CallActivityExecutor.EnterAsync` 持久化当前 child activation/spawn 身份；`TryCompleteAsync` 只匹配当前 activation 的 spawn，不按 parent token + activity name 取 `FirstOrDefault`。

### C7 #291 Comment-aware lookahead

Modeling generator 的 typed ViewField lookahead 跳过注释/Note token 后再判断 `[`、`{`、`=`；注释不改变 typed/untyped 分类。

验收：

- C1：DI 中不再注册 `ConcurrentDictionary<string, CancellationTokenSource>`；dispatcher、lease、operation service 全部通过 `IActiveExecutionRegistry` 注册/移除/取消。
- C2：Resource 与 Identity 的执行载体使用同一 `IHandledResponse<TResponse>` 契约；每个 Identity 操作只有一个 response carrier。
- C3：重复 canonical pattern/collection 在注册期失败；typed/polymorphic reference 解析目标确定；first-wins 不再存在。
- C4：generator 产出的 arity 类覆盖现有 16 个手写实现；调用签名与行为不变。
- C5：DCR 表覆盖 OIDC Registration §2 六项；DCR 与 discovery/authorize 的 response type 一致。
- C6：child completion 只匹配当前 activation 的 spawn；旧 activation 的 completion 被拒绝。
- C7：带注释的 ViewField 仍按 typed/untyped 正确分类。

## 工作流 D：SCIM

### D1 包与边界

首期新增两个可选包：

- `Schemata.Scim`：共享强制协议、schema registry、discovery、ListResponse、attribute mutability、error mapping。依赖 Abstractions/Common/Transport.Http，不依赖 Identity/Authorization。
- `Schemata.Identity.Scim`：Users、Groups 的 domain adapter、端点注册、Identity admission/事务映射。

`Schemata.Authorization.Scim` 属于后续扩展阶段：Applications 使用自定义 SCIM schema，不属于 RFC 7643 核心，不列入最小合规验收。首期包以 feature/extension 显式启用；Core 与其他 component 不引用它们。共享层单独成包的依据是协议实现只允许一份；domain adapter 分包的依据是 Identity 拥有 admission 与事务边界。

### D2 首期协议范围（强制功能）

- 基础路径：`/scim/v2`；媒体类型 `application/scim+json`；资源表示含 `schemas`、`id`、`meta`。
- Discovery：`GET /ServiceProviderConfig`、`GET /ResourceTypes`、`GET /Schemas` 及单项；返回 RFC 7643 §5/§6/§7 规定的形状。
- 资源操作：`POST` create、`GET` single、`GET` list、`PUT` replace、`DELETE`。PUT 只替换、不创建；DELETE 成功返回 204，删除后单项查询 404 且不再出现在列表。
- ListResponse：`totalResults` 必填；`Resources` 在非零时必填；`startIndex`/`itemsPerPage` 在分页时必填。`startIndex < 1` 按 1，`count` 为负按 0；返回数不超过 `count`。
- Attribute mutability：POST/PUT 忽略 `readOnly`；`readWrite`、`immutable`、`writeOnly` 按 RFC 7643 §2.2/§7 处理。
- Errors：RFC 7644 §3.12 的 `schemas`、`status`、`scimType`、`detail`；SCIM 端点使用 SCIM error shape，不复用 AIP-193 JSON。
- Authentication/Authorization：复用 host authentication 与现有 policy；SCIM 权限独立配置；不生成 provisioning principal。

首期不实现：PATCH（§3.5.2 OPTIONAL）、filter（§3.4.2.2 OPTIONAL）、sort（§3.4.2.3 OPTIONAL）、ETag/If-Match（§3.14 MAY）、Bulk（§3.7 OPTIONAL）、`/Me`、`.search`、attributes/excludedAttributes。`ServiceProviderConfig` 必须如实声明 `patch.supported=false`、`filter.supported=false`、`sort.supported=false`、`etag.supported=false`、`bulk.supported=false`。

### D3 Domain adapters（首期）

- Users：映射 `SchemataUser`/自定义 `TUser`；create/update/delete 走 Identity admission、uniqueness、confirmation、安全字段规则；`userName` 非空、全 provider 唯一、大小写不敏感；`active` 映射管理启用状态。
- Groups：映射角色与 membership；`displayName` 必填；members 的 `value` 使用 SCIM id，`$ref` 使用资源 location；PUT 在一个角色/membership 事务内替换成员集合；禁止通过 SCIM 提权到未授权角色。
- Applications：后续扩展阶段映射 `SchemataApplication` 与授权元数据；不暴露 client secret；保护 framework policy 字段；复用 application manager 与 repository mutation。
- 关联继续使用 canonical-name string；不引入 UID FK 或第二套 provisioning store。

### D4 证据与 surface matrix

- #127：真实 HTTP discovery、Users/Groups CRUD、ListResponse/pagination、error 正反例；文档声明未实现的 optional 子集。
- #128：SCIM HTTP 与现有 HTTP/gRPC 管理面的 per-operation parity matrix；SCIM 只在 HTTP 提供，gRPC 不声明等价。
- 测试使用 RFC 7643/7644 原文向量；文档声明实际支持的 SCIM 子集与限制。

## 工作流 E：RFC 8288 Web Linking

### E1 共享模型

在 `Schemata.Transport.Http` 增加单一 Web Linking 实现：

- `WebLink` 模型：target URI-Reference、rel、anchor、hreflang、media、title、title*、type、扩展参数。
- `LinkHeader` parser/serializer：RFC 8288 §3 ABNF、quoted-string 转义、逗号/分号、多个 link-value、相对 target 按 RFC 3986 §5 解析、anchor 覆盖 context。
- 一个 `ILinkContributor` DI seam：组件贡献 link，HTTP response shaping 统一序列化；组件不各自拼 header。

### E2 组件接入

| Issue | 组件 | Link 范围 |
|---|---|---|
| #197 | Resource | self、canonical、next/prev/first/last、related child collections |
| #198 | Scheduling | job/execution/owner 关系 |
| #200 | Push | subscription list self、provider-filtered variants |
| #201 | Flow | process/token/definition/catch 关系 |
| #202 | Authorization | Application/Scope/Token 管理资源 |
| #203 | Identity | User/Role；无 session 资源时不发明 link |
| #204 | Insight | query result self、next/prev |
| #205 | Report | definition/snapshot/chunk/schedule 关系 |
| #199 | Event | 保持 Won't Fix，不新增 |

### E3 边界

- Link header 只用于 HTTP；gRPC 不声明 RFC 8288 等价。
- Link 内容不包含 secret、token value、内部 tenant id。
- 相对 target 使用请求 URI 解析；anchor 与 context 按 RFC 8288 §3.1/§3.2 处理。
- 分页 link 使用现有 page token；不引入第二套分页状态。

## 工作流 F：清理与设计债

### F1 #343 包与表面清理

删除 `src/Schemata.Caching.Distributed/`、`src/Schemata.Entity.Owner/` 残留目录；移除 `Schemata.Tenancy.Skeleton` 未使用的 `Entity.Repository` 引用；修正 `AGENTS.md` 包数量；把 `RegistrationMetadataMapper.ResponseTypeRequirements` 移入 `AuthorizationConstants`；收敛 `ISimpleMapper` 的泛型/非泛型重复表面。

验收：残留目录不存在；`Schemata.Tenancy.Skeleton` 无未使用引用；response_type 常量只在 `AuthorizationConstants`；`ISimpleMapper` 只有一套实现表面。

### F2 #339 AdviceContext ambient

删除 `AdviceContext.Current`、`Require()`、`Establish()` 与残余 `Set/TryGet/Get/Has/Use` bag。dispatcher root 通过显式 `AdviceContext` 参数注入；handlers/advisors 迁移到显式 payload/DI，不保留静态 ambient 例外。

验收：不存在静态 per-request context；handlers 可在非 dispatcher scope 测试；嵌套与并行 context 互不影响。

## 工作流 G：通用实体替换

### G1 规则

- 每个含数据库实体的 component 在 `Use*` 入口暴露泛型实体参数。
- 采用子类型约束 `where T : SchemataX`；需要框架实例化时追加 `new()`。内置实体已实现所需字段与接口，用户子类型继承它们。
- 泛型实体参数直接使用实体名（`TActor`、`TJob`、`TSession`）；不给 entity 参数加 `Entity` 后缀。名称冲突时给 service 类型参数加后缀（`TSession` 与 `TSessionManager`）。
- 默认入口只把内置类型传给泛型实现，不复制第二套逻辑、不增加运行时 bool/flag 分支。
- 框架内部的 handler、advisor、repository、mutation、mapping、EF/LinqToDB model、resource metadata、Scheduling/Flow/Actor 运行时全部使用闭合的 `T`；component 内不得继续硬编码内置实体。
- 自定义类型必须由 host 在对应 repository/EF/LinqToDB 注册；shared-table 隔离要求该类型实现 `ITenant`。
- 辅助实体与主实体同等对待；不能只替换 `SchemataUser` 而遗漏 claim/login/token/role 等表。
- 跨 component 共享实体必须传播同一闭合类型：Security 的 `SchemataToken`/`SchemataSecurity` 被 Authorization 使用；Identity 的 user 被 Authorization.Identity 使用；Scheduling 的 job/execution 被 Flow/Push/Report 的 scheduling bridge 使用。bridge feature 接受上游类型参数，禁止各自定义第二套 token/job 实体。
- `Tenancy.Actor` 作为显式 bridge 时，`ActorStateStore`、`TenantActorSystem` 与 actor feature 必须接受闭合 `TActor`；`Actor.Foundation` 通过公开 runtime/factory seam 支持 bridge，不用 `InternalsVisibleTo` 把 Actor 的 internals 暴露给 Tenancy。

### G2 实体矩阵

| Component | `Use*` 入口 | 实体 |
|---|---|---|
| Actor | `UseActor<TActor>`；`ActorStateStore<TActor>` | `SchemataActor`；`Tenancy.Actor` bridge 同步泛型化 |
| Scheduling | `UseScheduling<TJob, TExecution>` | `SchemataJob`、`SchemataJobExecution` |
| Flow | `UseFlow<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>` | `SchemataProcess`、`SchemataProcessToken`、`SchemataProcessTransition`、`SchemataProcessCompensation`、`SchemataProcessParticipant`、`SchemataProcessSource`、`SchemataFlowEffectIntent` |
| Push | `UsePush<TPushSubscription>` | `SchemataPushSubscription` |
| Insight | `UseInsight<TInsightSource>` | `SchemataInsightSource` |
| Security | `UseSecurity<TSecurity, TToken>` | `SchemataSecurity`、`SchemataToken` |
| Identity | `UseIdentity<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole>` | 对应 `Schemata*` 实体；store 泛型参数保持 |
| Authorization | `UseAuthorization<TApplication, TAuthorization, TScope, TSubjectMapping>` | `SchemataApplication`、`SchemataAuthorization`、`SchemataScope`、`SchemataSubjectMapping` |
| Tenancy | `UseTenancy<TTenant, TTenantHost>` | `SchemataTenant`、`SchemataTenantHost` |
| Report | `UseReport<TReport, TSnapshot, TChunk>` | 已支持，保持 |

无持久化实体的 component（Event、Resource、Core、Caching、Messaging、Transport、Mapping、Validation、Modular、Advice、Abstractions、Common、Entity 基础设施）不增加实体泛型参数。

### G3 实施顺序

1. 先为每个 component 建立泛型 feature、DI 注册与闭合 repository/mutation。
2. 再迁移内部硬编码实体类型、handler、advisor、mapping 与 resource registration。
3. 最后让默认入口以内置类型调用泛型实现，并补每个 component 的自定义子类型端到端测试。

验收：每个矩阵行至少一个自定义子类型通过 `Use*` 完成 repository/EF/LinqToDB 映射与主要操作；默认内置类型行为不变；component 内不再有硬编码内置实体；不新增兼容 alias 或第二套非泛型实现；`FlowTenantRepositoryShould` 使用自定义 Flow 类型；`Tenancy.Actor` 使用自定义 Actor 类型完成 tenant 持久化。

## 依赖与顺序

1. **Wave A（P0）**：A1–A7。A1/A3/A4 可并行；A6 与 A7 独立。
2. **Wave B（Tenancy）**：B2 与 G 的实体闭合先于 B1/B3 的 tenant 注册；B4/B5/B6/B7 在 B1 后。
3. **Wave C（一致性）**：C1/C2/C3/C4/C5 独立；C6/C7 独立。
4. **Wave D（SCIM）**：D1 共享协议先于 D3 domain adapters；D4 依赖 D1+D3。
5. **Wave E（RFC 8288）**：E1 先于 E2；各组件 slice 可并行。
6. **Wave F（清理与验收）**：F1/F2 + 文档 + 集中 build/test。
7. **Wave G（实体替换）**：G1 先于各 component 的 G2/G3；G3 与 B2/B3 同步收口。

## 验收

- P0：token veto 双 provider、probe.log 只读内容根、scheduling staging 失败、provider cache 并发/取消、SessionCache 慢后端、auth 并发、JsonDocument 异常路径。
- Tenancy 边界：默认实体无 `ITenant`；自定义 `ITenant` 类型经 feature 注册后隔离；分库不要求 `ITenant`；scheduling 无 adapter、无 tenant 探测；per-tenant 调度由用户 tenant 注册提供；无 `Tenancy.Foundation → Scheduling`；`TenantContext`/`TenantIdentity`/`TenantResolveException` 位于 `Tenancy.Skeleton`；非 Tenancy component 无 tenant 探测或 scope 选择。
- 实体替换：G2 每一行至少一个自定义子类型完成 `Use*` 注册、repository/EF/LinqToDB 映射和主要操作；默认内置类型行为不变；`Tenancy.Actor` 使用自定义 Actor 类型持久化。
- SCIM：discovery、Users/Groups 的 POST/GET/PUT/DELETE、ListResponse/pagination、SCIM error shape、attribute mutability 正反例；未实现的 optional 能力在 `ServiceProviderConfig` 如实声明；权限拒绝；#127/#128 证据文档。
- RFC 8288：parser/serializer round-trip；quoted/多重 link/相对 target/anchor；各组件 HTTP 响应 header 可被参考 parser 解析；无重复序列化。
- 回归：AIP standard/custom methods、canonical-name refs、OAuth/OIDC 现有测试保持。
