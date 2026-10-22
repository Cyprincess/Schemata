# 框架审查修复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 2026-10-09 审查确认的 80 个问题(#206–#285)及已有 issue 的审查补充，并把多租户改为由 `Schemata.Tenancy.*` 持有的非侵入式机制。

**Architecture:** 以已批准设计 `docs/superpowers/specs/2026-10-09-framework-review-remediation-design.md` 为准：统一被动 `ITenant` 数据trait；repository advisors、cache/protection decorators、消息 propagator/scope factory、actor 系统适配全部由 Tenancy 持有；其余 component 只消费通用扩展点。各 owner 分组独立成 slice，Tenancy cutover 覆盖 Flow/Scheduling/Actor 全部租户数据字段与调用点。

**Tech Stack:** .NET 10/8, Schemata.slnx, xUnit v2 + Moq, EF Core + LinqToDB 双provider, RabbitMQ(CI外不要求运行broker)。

**Spec:** `docs/superpowers/specs/2026-10-09-framework-review-remediation-design.md`

## Global Constraints

- 保留 Authorization `RelaxXXX` API、配置与显式降级行为；修复默认/绑定/并发/错误路径。
- 保留 canonical-name OIDC `sub`；不新增 Oid/UID投影/UID FK/兼容读取/迁移工具。
- `[ResourceReference]` 保持 string 完整 canonical-name 关联(单目标与 canonical-name 解析的多目标)；不把 ref 换成 UID。
- 租户逻辑只存在于 `Schemata.Tenancy.*`；其他组件不读 TenantContext、不 stamp/比较 TenantUid、不拼租户缓存键、不解析租户消息 key、不选择 tenant scope。
- 每issue先读当前源文件；审查记录(`local://review-20261009-final-delivery.json`)不是自动 codemod 指令。
- 不引入 alias、双实现、双写、startup环境guard、遥测、outbox、leader election；不承诺 exactly-once。
- 不提交 `.superpowers/`、`docs/superpowers/`、`docs/audits/`、`benchmarks/`；不编辑 generated changelog 段；不改 `eng/common/**`。

## Review Focus

1. Tenant 切换后 continuation/broker replay/actor ref 被另一租户接受 — 每个边界都有真实拒绝路径测试。
2. 未启用 Tenancy 时 host-only 行为保持原样 — 无租户服务仍可构建运行。
3. `MutationResult.NoWrite` 被映射为 201/changed — Create/Update/Delete 与 idempotency 使用同一 disposition。
4. 并发路径: device poll、recovery code、cache slot、call-activity re-entry、job claim/renew — 竞争输入有确定结果。
5. 双provider(EF Core/LinqToDB)对 tenant 谓词/索引/JSON shape 的可翻译性 — 每个 provider 至少一条真实查询测试。

---

## Wave 1: 独立 owner 修复(可并行)

### Task 1: Kernel/Transport (#207,212,231,233,234,236,237,250,262,265,279)

**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Authentication/SchemataAuthenticationHandler.cs:445-470`
- Modify: `src/Schemata.Transport.Grpc/Interceptors/ExceptionMappingInterceptor.cs:22-56`
- Modify: `src/Schemata.Core/Building/SchemataResourceBuilder.cs:179-237`
- Modify: `src/Schemata.Core/Extensions/SchemataOptionsExtensions.cs:79-128`; `src/Schemata.Core/SchemataOptions.cs:44-56`
- Modify: `src/Schemata.Core/Features/SchemataJsonSerializerFeature.cs:22-30`
- Modify: `src/Schemata.Abstractions/Globalization/AcceptLanguageParser.cs:49-58`
- Modify: `generators/Schemata.Advice.Generator/AdvicePipelineGenerator.cs`
- Modify: `src/Schemata.Transport.Grpc/Interceptors/ExceptionMappingInterceptor.cs`; `src/Schemata.Transport.Http/Extensions/StreamEndpointExtensions.cs`
- Modify: `src/Schemata.Security.Foundation/Signatures/{SignatureBase,HttpMessageSignatureVerifier,HttpSignatureAlgorithm,MessageComponentIdentifier,HttpSignatureMessageExtensions}.cs`
- Test: `tests/Schemata.Authorization.Tests/DPopChallengeShould.cs`; `tests/Schemata.Core.Tests/Core/ConfiguratorsShould.cs`; `tests/Schemata.Security.Foundation.Tests/Signatures*Should.cs`; 各 owner 现有测试文件
- Docs: `docs/documents/authorization.md:771`; `docs/documents/tenancy.md` priority 表(如需要)

**Interfaces:**
- Produces: `SchemataOptions` 首注册共享 feature 字典；serializer feature 由 DI 而非 HasFeature 决定 MVC 配置。Wave 2+ 不依赖本 task 内部细节。

- [ ] **Step 1: 逐issue读当前源文件 + review 证据**(`local://review-20261009-final-delivery.json` 对应条目)。
- [ ] **Step 2: 写行为测试** — Bearer challenge auth-param(#207: missing token 不产生 `invalid_token`, per RFC 6750 §3)、streaming/unary cancellation 单一语义(#212,#250)、declared endpoints 保留(#231)、first-registration feature 依赖(#265)、JSON options 独立于 feature flag(#265)、forwarded headers DI 路径(#233)、two-parameter configurator(#234)、advice generator 可访问性(#237)、Accept-Language 非有限 q(#262)、signature bytes/curve/age/quoted fields(#279)。
- [ ] **Step 3: 实现** — 每个 issue 在现有 owner 内最小修复；signature 修复共享一个 quoted-field scanner。
- [ ] **Step 4: 运行本组测试一次** — `dotnet test tests/<project> -c Release --no-build`(integration owner 先统一 build)。
- [ ] **Step 5: Commit** — `git add <task-owned files>` + `git commit --no-gpg-sign -m "fix(<scope>): <subject>" -m "<body>"`, `cog check HEAD~1..HEAD`。

### Task 2: Expressions/Data/Mapping (#206,208,209,221,226,238,252,253,254,255,256,257,258,259,261,267,272,274)

**Files:**
- Modify: `src/Schemata.Expressions.Aip/{AipCompileVisitor,AipCompiler,AipParser}.cs`
- Modify: `src/Schemata.Expressions.Cel/{CelCompileVisitor,CelCompiler,CelParser,CelValues}.cs`
- Modify: `src/Schemata.Expressions.Skeleton/DynamicValues.cs`
- Modify: `src/Schemata.Entity.LinqToDB/SystemComponentModelDataAnnotationsSchemaAttributeReader.cs:57-82`
- Modify: `src/Schemata.Mapping.Skeleton/MaskTree.cs`
- Modify: `src/Schemata.Abstractions/Resource/RawJsonConverter.cs`
- Modify: `src/Schemata.Entity.EntityFrameworkCore/{SchemataModelCustomizer.cs,Conversions/JsonValueComparers.cs}`
- Modify: `src/Schemata.Entity.Repository/{QueryCacheKey.cs,Advisors/AdviceAddValidation.cs,Advisors/AdviceUpdateValidation.cs}`
- Modify: `src/Schemata.Resource.Foundation/Advisors/ValidationHelper.cs`
- Modify: `src/Schemata.Entity.Repository/CompatibilitySuppressions.xml`(如 API 变更)
- Modify: `generators/Schemata.Modeling.Generator/{Parser.cs,Generators/EnumGenerator.cs}`
- Test: `tests/Schemata.Expressions.Cel.Tests/*`; `tests/Schemata.Expressions.Aip.Tests/*`; `tests/Schemata.Entity.LinqToDB.Integration.Tests/*`; `tests/Schemata.Mapping.Tests/*`; `tests/Schemata.Entity.EntityFrameworkCore.Integration.Tests/NestedColumnShould.cs`; `tests/Schemata.Modeling.Generator.Tests/*`
- Docs: `docs/documents/entity/query-cache.md:90-94`; `docs/modeling/enums.md`; `docs/modeling/grammar.md`

**Interfaces:**
- Produces: parser-owned AST identity(#256): cache admission 由 parser provenance + structural fingerprint 决定,不信任 public mutable `Source`。validation Handle 语义统一(#272)。

- [ ] **Step 1: 读源+证据;检查 #261 已有 generic canonical-JSON comparer 行为不被破坏(#206 只改 converter precedence)。**
- [ ] **Step 2: 写行为测试** — 数值 identity(#208: DynamicValues 精确 long/decimal;#257: typed promotion 不缩窄)、AST cache(#256: 两个 parser-direct 空 Source 树不互串)、map/key contract(#238)、duration range(#258)、macro 错误传播(#259)、TableAttribute.Schema(#252)、mask ancestor(#253)、RawJson token type(#254)、AIP field/literal(#255)、literal exception normalization(#209)、enum emission(#221)、language defaults(#226)、JSON dictionary hash(#261)、query cache key(#267)、validation Handle(#272)。
- [ ] **Step 3: 实现于现有 owner;语言语义保持 AIP/CEL 各自规则,不加第二 compiler。**
- [ ] **Step 4: 运行本组测试一次。**
- [ ] **Step 5: Commit**(同上模式)。

### Task 3: Authorization/Identity (#214,223,224,225,228,229,239,240,241,242,243,244,245,246,247,260,268,275,276,280)

**Files:**
- Modify: `src/Schemata.Authorization.Foundation/{Services,Handlers,Advisors,Controllers,Authentication}/**/*.cs`
- Modify: `src/Schemata.Authorization.Identity/IdentityHostSessionStore.cs`
- Modify: `src/Schemata.Identity.Foundation/Handlers/IdentityHandler.cs`; `src/Schemata.Identity.Skeleton/Stores/SchemataUserStore.cs`
- Modify: `src/Schemata.Security.Foundation/Stores/CacheTokenStore.cs`
- Test: `tests/Schemata.Authorization.Tests/*`; `tests/Schemata.Authorization.Integration.Tests/*`; `tests/Schemata.Identity.Tests/*`
- Docs: `docs/documents/authorization.md`; `docs/documents/identity.md`; `docs/documents/authorization.md:473`(bearer audience #275/#228 说明)

**Interfaces:**
- Produces: OP session evidence 单一 authority(#244);pairwise framing(#247);grant deadline 一致(#280);token-slot 原子 establishment(#260)。

- [ ] **Step 1: 读源+证据。#224 只修 expired-Redeemed replay 路径(#64),不动 Relax;#228 只改文档+数据契约说明,不动 sub。**
- [ ] **Step 2: 写行为测试** — expired-Redeemed replay 家族失效(#224)、max_age+boundary 同时满足(#239)、DenyAsync Type 检查(#239)、DPoP PathBase(#240)、device poll 单窗口(#241)、recovery code 并发单兑换(#242)、IdentityResult 传播(#243)、session mirror/authority(#244)、registration 默认 method(#245)、participant publication boundary(#246)、pairwise framing/claim metadata(#247)、scope claims 全量(#276)、grant deadline(#280)、resource disposal(#268)、issuer 适配(#214,#225)、senders(#223)、audience(#229)。
- [ ] **Step 3: 实现于现有 owner。**
- [ ] **Step 4: 运行本组测试一次。**
- [ ] **Step 5: Commit。**

### Task 4: Resource/Insight/Report/Push (#210,211,213,216,222,227,235,248,273,278)

**Files:**
- Modify: `src/Schemata.Resource.Foundation/ResourceOperationHandler.{Create,Update,Delete}.cs`
- Modify: `src/Schemata.Insight.Foundation/{Execution/PlanExecutor.cs,Execution/LocalPipelineExecutor.Limit.cs,SchemataInsightBuilder.cs,Planning/InsightPlanBuilder.cs}`
- Modify: `src/Schemata.Report.Foundation/{Handlers,Definitions,Snapshots,Jobs}/**/*.cs`
- Modify: `src/Schemata.Push.Foundation/Handlers/AddPushSubscriptionHandler.cs:48-55`
- Modify: `src/Schemata.Report.Grpc/ReadSnapshotGrpcResponse.cs`
- Test: `tests/Schemata.Resource.Tests/*`; `tests/Schemata.Insight.Tests/*`; `tests/Schemata.Report.Tests/*`; integration 对应
- Docs: `docs/documents/resource/{filtering,naming,http-transport,grpc-transport}.md`; `docs/documents/insight/*`; `docs/documents/report/*`

**Interfaces:**
- Produces: `MutationResult` disposition 贯穿 Resource handlers(#227);snapshot Uid 进入 continuation(#248);Insight 语言 profile 复用共享 resolver(#213)。

- [ ] **Step 1: 读源+证据。#248 的 durable report 身份在 Tenancy Wave 之后才能落 — 本 task 只做 snapshot Uid/definition/job ownership/空流取消等不依赖租户的项;durable principal 标记 blocked-by Wave 5。**
- [ ] **Step 2: 写行为测试** — NoWrite disposition(#227)、repeated Delete age(#211)、residual scan owner(#210)、profile enforcement(#213)、consumer naming(#216)、docs 对齐(#222,#273)、Pending settle(#235)、snapshot Uid continuation(#248)、wildcard schema(#278)。
- [ ] **Step 3: 实现。**
- [ ] **Step 4: 运行本组测试一次。**
- [ ] **Step 5: Commit。**

### Task 5: Flow/Actor/Event (#215,218,219,220,230,232,249,251,269,270,271,281,282,283,284,285)

**Files:**
- Modify: `src/Schemata.Flow.Bpmn/{BpmnEngine.cs,Runtime/**}`; `src/Schemata.Flow.StateMachine/StateMachineEngine.cs`
- Modify: `src/Schemata.Flow.Skeleton/{Builders,Models,Runtime,Utilities}/**/*.cs`
- Modify: `src/Schemata.Flow.Event/Handlers/*.cs`; `src/Schemata.Event.Skeleton/IEventBus.cs`
- Modify: `src/Schemata.Event.Foundation/Runtime/{HandlerResolver,InProcessEventBus}.cs`
- Modify: `src/Schemata.Messaging.RabbitMq/Runtime/{RabbitMqRequestDispatcher,RabbitMqRequestConsumerHost}.cs`; `src/Schemata.Event.RabbitMq/Runtime/RabbitMqConsumerHost.cs`
- Modify: `src/Schemata.Actor.Foundation/Runtime/RequestDispatchingActor.cs`
- Test: `tests/Schemata.Flow.*.Tests/*`; `tests/Schemata.Event.Tests/*`; `tests/Schemata.Messaging.RabbitMq.Tests/*`; `tests/Schemata.Actor.Tests/*`
- Docs: `docs/documents/flow/*`; `docs/cookbook/flow-with-events.md`(#220)

**Interfaces:**
- Produces: activation lineage 选择(`Spawner` chain)用于 join/resume/terminate/call-completion(#269,#285);Ask declared contract(#283);timer generation envelope(#219 相关项)。

- [ ] **Step 1: 读源+证据。租户相关 Flow 行为(stamp/filter)不在本 task — 归 Wave 5。**
- [ ] **Step 2: 写行为测试** — activation-aware join/resume/terminate(#269)、call re-entry(#285)、guard/payload/loop/compensation(#270)、graph admission 嵌套(#271)、projection warning 保留(#281)、leave/boundary scope(#282)、Ask declared contract(#283)、canceled delivery(#284)、source-bearing contract(#249)、broker decode/settlement/scope(#251,#219,#218)、event eligibility(#284)、timer stale firing(#219 相关)、interrupted escalation(#232)、ParallelDefinition(#230)、catch-all enumerable(#215)、warning dedup(#281)。
- [ ] **Step 3: 实现;`Spawner`/transition 现有身份优先于新增状态位。**
- [ ] **Step 4: 运行本组测试一次。**
- [ ] **Step 5: Commit。**

### Task 6: Scheduling 核心修复(#217,263,264,277)

**Files:**
- Modify: `src/Schemata.Scheduling.Foundation/{JobExecutionDispatcher,DefaultOperationService}.cs`
- Modify: `src/Schemata.Scheduling.Foundation/Handlers/{DefaultScheduleJobHandler,DefaultUnscheduleJobHandler,DefaultTriggerJobHandler}.cs`
- Modify: `src/Schemata.Scheduling.Foundation/{SchedulingInitializer,WaitOperationHandler,RunJobHandler}.cs`; `src/Schemata.Scheduling.Foundation/Runtime/{DefaultScheduler.Schedule,DefaultScheduler.Trigger,DefaultScheduledJobRegistry}.cs`
- Test: `tests/Schemata.Scheduling.Tests/*`; integration 对应

**Interfaces:**
- Produces: lease 覆盖 setup+body(#263);inline LRO 使用现有 lease/cancellation owner(#263);timer 提交后替换(#264);execution.Job 规范 canonical(#264)。

- [ ] **Step 1: 读源+证据。租户 stamp/Tenant 字段归 Wave 5。**
- [ ] **Step 2: 写行为测试** — claim 覆盖全生命周期(#263)、failure pacing(#263)、OCE 过滤(#263)、inline cancel(#263)、orphan recovery 语义(#263,不凭年龄猜 alive)、timer commit 顺序(#264)、canonical job ref(#264)、TimeProvider 传播(#264)、CreateAsyncScope(#264)、registry bijective(#277)、reminder terminal(#217)。
- [ ] **Step 3: 实现。**
- [ ] **Step 4: 运行本组测试一次。**
- [ ] **Step 5: Commit。**

---

## Wave 2: Tenancy 通用 seam 建立(串行,阻塞 Wave 3-5 的租户部分)

### Task 7: 被动 ITenant trait + Messaging/Execution 通用化

**Files:**
- Create: `src/Schemata.Abstractions/Entities/ITenant.cs`
- Modify: `src/Schemata.Abstractions/Entities/ICanonicalName.cs`(如需 suppress tenant 于 mask;视实现)
- Modify: `src/Schemata.Messaging.Skeleton/{MessageContexts,MessageExecutionScope,StreamExecutionContext}.cs`; `src/Schemata.Messaging.Skeleton/Runtime/MessageExecutionScopeFactory.cs`
- Modify: `src/Schemata.Tenancy.Messaging/{TenantMessageExecutionScopeFactory.cs,TenantMessagingExtensions.cs}`; Create: `src/Schemata.Tenancy.Messaging/TenantMessageContextPropagator.cs`
- Test: `tests/Schemata.Messaging.Skeleton.Tests/*`(如无则创建于 `tests/Schemata.Core.Tests/`); `tests/Schemata.Tenancy.*.Tests`

**Interfaces:**
- Consumes: 现有 `IMessageContextPropagator` enumerable; `IMessageExecutionScopeFactory`。
- Produces: `ITenant { Guid? TenantUid { get; set; } }`; tenant-neutral `MessageExecutionScope(scope, Func<IDisposable> enter)`; `MessageContexts.Capture(IServiceProvider)` 只走 propagator 不再绑定 tenant; Tenancy propagator 写/读 `tenancy.tenant-id`。

- [ ] **Step 1: 写失败测试** — ITenant 存在性非必需;Capture 无 Tenancy 时 Items 为空;host factory 接受任意 context;scope Enter 释放 lease;stream 每次枚举恢复捕获上下文。
- [ ] **Step 2: 运行确认失败。**
- [ ] **Step 3: 实现 trait 与 Messaging.Skeleton 去租户化;Tenancy.Messaging 提供propagator/factory。**
- [ ] **Step 4: 运行测试通过。**
- [ ] **Step 5: Commit。**

### Task 8: Tenancy Repository advisors + Cache/Protection decorators

**Files:**
- Create: `src/Schemata.Tenancy.Foundation/Repository/AdviceAddTenant.cs`; `AdviceBuildQueryTenant.cs`; `AdviceUpdateTenant.cs`; `AdviceRemoveTenant.cs`; `TenantRepositoryExtensions.cs`
- Modify: `src/Schemata.Tenancy.Foundation/SchemataTenancyBuilder.cs`; `src/Schemata.Tenancy.Foundation/Schemata.Tenancy.Foundation.csproj`(加 Entity.Repository 依赖,如无)
- Create: `src/Schemata.Tenancy.Foundation/Protection/TenantDataProtectionProvider.cs`; `TenantDataProtectionExtensions.cs`
- Modify: `src/Schemata.Tenancy.Caching/TenantCacheProvider.cs`(ctor 注入 accessor 替代 ambient)
- Test: `tests/Schemata.Tenancy.Foundation.Tests/*`(Create/扩展); `tests/Schemata.Tenancy.Caching.Tests/*`

**Interfaces:**
- Consumes: Task 7 `ITenant`; 现有 `IRepository{Add,BuildQuery,Update,Remove}Advisor<>`; `ICacheProvider` wrapper seam; `IDataProtectionProvider`。
- Produces: `UseRepositories()` 注册 advisor 链;`AddTenantDataProtection()` 包装 protector purpose;cache decorator 保持单 outlet 原子性。

- [ ] **Step 1: 写失败测试** — add stamp(after canonical-name)、query filter(可翻译表达式)、update/remove 跨租户明确失败(非 NoWrite)、host 默认只见 host 行、protection cross-tenant Unprotect 抛 CryptographicException、cache framing per tenant。
- [ ] **Step 2: 确认失败。**
- [ ] **Step 3: 实现 advisors 与 decorators;启用才注册。**
- [ ] **Step 4: 通过。**
- [ ] **Step 5: Commit。**

---

## Wave 3: 租户实体与调用点 cutover(依赖 Task 7/8,可按 owner 并行)

### Task 9: Flow 租户 cutover

**Files:**
- Modify: `src/Schemata.Flow.Skeleton/Entities/{SchemataProcess,SchemataProcessToken,SchemataProcessTransition,SchemataProcessParticipant,SchemataProcessSource,SchemataProcessCompensation}.cs`(ITenant adoption;Source/Compensation 覆盖 direct persisted 子记录)
- Modify: `src/Schemata.Flow.Foundation/{FlowPersistenceScope,FlowAccessPolicy,FlowParticipantManager,ProcessPersistence,FlowReadPolicy,FlowHandlerSupport}.cs`(删除所有 TenantContext 读写与谓词)
- Test: `tests/Schemata.Flow.Tests/*`; `tests/Schemata.Flow.Integration.Tests/*`

- [ ] **Step 1: 失败测试** — 双租户 process 隔离、participant 隔离、Source/Compensation 子记录隔离、host 只见 host process、跨租户 update 失败。
- [ ] **Step 2: 确认失败。**
- [ ] **Step 3: 删除 stamp/filter;实体采纳 ITenant;FlowReadPolicy 去租户分支(参与谓词保留)。**
- [ ] **Step 4: 通过。**
- [ ] **Step 5: Commit。**

### Task 10: Scheduling 租户 cutover

**Files:**
- Modify: `src/Schemata.Scheduling.Skeleton/Entities/{SchemataJob,SchemataJobExecution}.cs`(string Tenant → ITenant Guid? TenantUid;index 更新)
- Modify: `src/Schemata.Scheduling.Foundation/Handlers/*.cs`; `src/Schemata.Scheduling.Foundation/{DefaultOperationService,JobExecutionDispatcher,SchedulingHandlerSupport}.cs`(删除 stamp/Tenant 比较/host Enter;持久化通用 MessageContext 快照)
- Modify: `src/Schemata.Tenancy.Foundation/Scheduling/TenantJobExecutionAdapter.cs`(Create;Tenancy-owned host 枚举+scope 选择,采用现有 IJobExecutionStore/factory 边界)
- Test: `tests/Schemata.Scheduling.Tests/*`; integration

- [ ] **Step 1: 失败测试** — 双租户 job/execution 隔离、host dispatcher 经 Tenancy adapter 枚举全部并逐行恢复 scope、Tenant 字段不再存在于 Scheduling 代码、重启恢复正确 scope。
- [ ] **Step 2: 确认失败。**
- [ ] **Step 3: cutover;`MessageContext` 快照由 job 创建时捕获。**
- [ ] **Step 4: 通过。**
- [ ] **Step 5: Commit。**

### Task 11: Actor 租户 cutover

**Files:**
- Modify: `src/Schemata.Actor.Skeleton/ActorId.cs`(删 TenantIdentity); `src/Schemata.Actor.Skeleton/Entities/SchemataActor.cs`(ITenant)
- Modify: `src/Schemata.Actor.Foundation/Runtime/{ActorStateStore,ActorInstance,InProcessActorSystem}.cs`(删 tenant 检查/Bind)
- Modify: `src/Schemata.Actor.Scheduling/Runtime/ActorReminders.cs`(去 tenant 序列化;typed ActorReminderKey)
- Create: `src/Schemata.Tenancy.Actor/TenantActorSystem.cs` + csproj(如依赖图需要;否则置于 Tenancy.Foundation)
- Test: `tests/Schemata.Actor.Tests/*`; `tests/Schemata.Actor.Scheduling.Tests/*`

- [ ] **Step 1: 失败测试** — decorator per-tenant runtime、actor-ref 捕获 context 恢复、跨租户 ref 拒绝、shutdown/idle 完整、reminder key 无租户序列化。
- [ ] **Step 2: 确认失败。**
- [ ] **Step 3: cutover + Tenancy decorator。**
- [ ] **Step 4: 通过。**
- [ ] **Step 5: Commit。**

### Task 12: Core/Resource/Report/Insight 租户残余清理

**Files:**
- Modify: `src/Schemata.Core/Extensions/ServiceCollectionExtensions.cs:233`(rate-limit subject 经 accessor)
- Modify: `src/Schemata.Resource.Foundation/{Models/PageToken.cs,ResourceOperationHandler.List.cs}`(删 Tenant 字段/读取/比较)
- Modify: `src/Schemata.Report.Foundation/Handlers/ReportReadPageToken.cs`; `src/Schemata.Insight.Foundation/Execution/InsightPageToken.cs`(同)
- Test: `tests/Schemata.Resource.Http.Integration.Tests/ResourceContinuationBindingShould.cs`(现有 cross-tenant 测试保持通过); `tests/Schemata.Tenancy.*.Tests`

- [ ] **Step 1: 失败测试** — cross-tenant continuation 仍被拒(经 protector)、Core 无 TenantContext 引用。
- [ ] **Step 2: 确认失败。**
- [ ] **Step 3: 删除字段与读取;验证 cookie/antiforgery/login continuation roundtrip(Task 8 protector 已装)。**
- [ ] **Step 4: 通过。**
- [ ] **Step 5: Commit。**

---

## Wave 4: 集成收尾

### Task 13: 文档/CHANGELOG/shipped docs + 清理

**Files:**
- Modify: `docs/documents/**/*.md`(已批准设计列出的全部文档对齐项)
- Modify: `docs/documents/tenancy.md`(非侵入式架构说明)
- Delete: throwaway scripts(如有)

- [ ] **Step 1: 按设计§统一实现约束改写文档;`cog check` 不涉及 docs。**
- [ ] **Step 2: 清理 scaffolds。**
- [ ] **Step 3: Commit(docs 单独提交,不混产品代码)。**

---

## Wave 5: 统一验证

### Task 14: 统一 build + 测试 + smoke

- [ ] **Step 1:** `dotnet build Schemata.slnx -c Release`(integration owner 一次)。
- [ ] **Step 2:** `dotnet test Schemata.slnx -c Release --no-build`(一次;只重跑实际暴露 task-owned 失败的项)。
- [ ] **Step 3:** 真实 smoke:双租户 HTTP host 续页/幂等/后台 job 恢复;单租户无 Tenancy host 原行为。Broker 不可用时说明未运行 surface,不造输出。
- [ ] **Step 4:** reviewer agent 审查整个 diff vs 本计划;修正审查发现的偏差。
