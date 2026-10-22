# 高价值框架审查问题修复计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 00c34ae07（含）至 0a8372e1 审查中的全部优先问题、框架一致性问题及安全／持久化／主要消费者路径上的高影响问题；不扩展产品能力。

**Architecture:** 保持一个 dispatcher、一个业务 handler、一个 repository mutation/UoW owner、一个 cache idempotency owner。调用者入口执行现有 authentication/authorization；系统自动环节不生成、不注入 principal，以现有 job/timer/subscription/token/generation 生命周期条件推进。显式请求／stage payload 传递数据，DI/keyed/enumerable advisor 注册决定可选能力。

**Tech Stack:** .NET 8/10、ASP.NET Core DI、xUnit v2 + Moq、EF Core/LinqToDB、现有 Memory/Redis cache、RabbitMQ transport、AutoMapper/Mapster。

**Spec:** 本计划“范围与已确定决策”是本轮设计基线；既有 `docs/superpowers/specs/2026-10-09-framework-review-remediation-design.md` 只复用不冲突的组件边界，不重新实施已完成的旧问题。证据：`local://review-20261012-issues.json`、`local://review-20261012-candidates.json`、tekoki issues 对应 HEAD 条目。

## 授权与执行边界

- 本轮授权是制定计划，不是修改产品代码。状态：**待用户审阅；未执行**。
- 基线 `0a8372e137d15af40ce9917e99eb94d714915343`。执行前核对当前 checkout 和任务相关用户改动，不把旧 issue 当自动修改指令。
- 本文件不提交、不推送；不覆盖既有 2026-10-03/09 计划。后续实施提交仅包含任务拥有文件。
- 用户上轮免除 build/tests/smoke/spike 的要求用于审查；本轮不运行这些命令。下文列出实施验收，但执行方式需用户批准。

## Global Constraints

- 系统自动环节不接入或产生 principal。需要业务身份时由框架用户的 job 定义／job handler／Flow script／内联业务代码处理。删除 SchedulingSystemPrincipal、FlowSystemPrincipal 及 ReferenceEquals bypass；不换成 DI-generated system principal、魔法 claim、全局 null-as-trusted 或 isSystem 标志。
- 保留调用者入口的 authentication/permission/instance eligibility；自动推进并不等于匿名 HTTP/RPC 用户可调用同一动作。
- 幂等性复用现有 cache。只有确认未写入／回滚的请求释放 owned reservation；已提交或提交结果未知不允许重复执行。数据库与 cache 非原子，不承诺 crash-safe exactly-once。
- canonical-name STRING 关联、单类型／多态 ref、OIDC canonical-name sub 保留；不引入 UID FK 或自动 Name fallback。
- Authorization RelaxXXX 的 API 和显式降级行为保留。
- 租户逻辑严格由 Schemata.Tenancy.* 持有。其余组件只消费通用生命周期／DI seam；Report→Insight 为既定例外。
- 不新增包，不新增第二 issuer/store/dispatcher/compiler/mapper/actor mailbox；不加兼容别名、双写、迁移工具、startup guard、遥测或无关重构。
- 可选能力通过 GetService/GetRequiredService/GetServices/keyed/advisor composition 决定，不能用 ambient marker/bool 分路。
- Pure string constants 归 SchemataConstants 或组件 Constants。真实 enum/domain state、泛型 advisor 的排序 companion 不因形状相似机械合并。
- 测试观察最终业务结果、持久化行、调用拒绝、竞态胜负、scope disposal；禁止 descriptor count、ctx marker、cache key 字符串或 mock echo 成为唯一断言。

## Review Focus

1. commit 后 response/cache/observer 失败：不得释放 reservation 后重复业务写入。
2. 系统 timer/event 自动推进与外部用户调用：同一业务 owner，不同 admission 责任，不能因 principal=null 全局放行。
3. nested tenant scope 与 retained actor ref：父 scope/cache entry 退休后仍保证独立 lease 与单 owner。
4. MutationResult.NoWrite：token、scheduler、undelete 不伪造持久化成功；已有合法业务 no-op 不一刀切改成异常。
5. DTO/entity mapping 与 field mask：unmasked mutable object/collection 不被完整映射的原地修改污染。

## 范围与价值选择

### 必须完成

- 优先修复：#324、#318、#287、#288、#286、#289、#323。
- 一致性：#181、#144；移除系统 principal；explicit advisor payload/handled result；组件边界／constants 放置；#227 的剩余 Undelete disposition。

### 本轮纳入的高影响问题

|领域|纳入|价值／影响|
|---|---|---|
|Repository/Data|#301,#302,#303,#297|scope 错配、transaction 失败后继续非事务写、key lookup 失败、cache key 互相覆盖|
|Identity/Auth|#294,#295,#313；#244/#247/#268 本轮补充|管理凭证拒绝、plaintext secret 串调用、联系方式变更失败、session subject 绑定、pairwise 配置一致、crypto lifetime|
|Resource/Mapping|#299,#300,#317；#227 剩余|query widening、非法 canonical 名、masked update 修改未选字段、未写入伪成功|
|Scheduling/Report|#319,#320,#321,#328；#248 本轮补充|durable operation 不存在／缺参数／永不 done、timer 泄漏、成功结果变失败、取消结果错误|
|Tenancy/Actor|#290,#330|runtime 永久增长、nested scope provider 过早 dispose／双 owner 风险|
|Insight|#314,#315,#316|数字精度、limit 超读触发 scan cap、公开投影漏字段|
|Expressions|#298,#304,#305,#307,#308,#325,#326,#327,#332；#258 本轮 offset 补充|过滤执行错误、缓存串语义、typed/value 路径分裂、合法 map/macro 失败|
|Flow|#309,#310,#311,#312；#270 本轮自动 throw/catch 路由补充|残留订阅、历史 terminal outcome 改写、重复意图、payload admission 错误、自动路由不同语义|
|Transport/Signatures|#306,#331,#322；#279 本轮 escaped-path 补充|失败丢失、observer 跳过、Unicode/签名输入不一致|
|Generator/Tests|#292,#293,#329|合法 DSL 输出无效／诊断崩溃、failure simulator 污染 durable-effect 结论|
|Errors/Docs|#133 ValidationException 补充；#333 与本轮改动相关内容|错误事实／locale 丢失、消费者 recipe 无法运行|

### 明确暂缓

- #291 comment lookahead：低频 DSL 语法边界，保留 issue；不阻塞主要路径修复。
- #296 JsonDocument：可达但低影响资源释放，保留独立 issue；不与安全切换混合。
- #333 与本轮无关的 overview/guide 编号／措辞调整：只修此次受影响示例和契约；不进行全站改写。
- registration finite-lifetime rotation：上一审查依赖注释推导固定 deadline，现有 lifetime policy 不能从注释确定；不擅改为固定／滑动期限。
- logout script terminator、cache conversion fingerprint、其余仍缺消费者可达／依赖语义证据的候选：不自动修、不新增 defensive guard。
- 本轮不实施整个旧 #206–#285 backlog，只处理本轮确认的剩余项。已完成代码不因旧 issue open 状态恢复旧路径。

## 依赖与波次

1. **Wave A：必需写入、事务、注册、broker ownership**（Task 1–4）。互不共享源文件的任务可并行；integration owner 最多派四个实现者。
2. **Wave B：principal clean cutover、显式 stage 数据、幂等性结算**（Task 5–7）。Task 7 依赖 Task 1/2/6；Task 5 和 Task 6 占有不同 owner，集成者协调 shared resource/security registration。
3. **Wave C：durable lifecycle、tenant lease、数据投影**（Task 8–12）。Task 8 依赖 Task 1/5；Task 9 依赖 Task 6/7；Task 10 先 nested lease 后 actor lifetime。
4. **Wave D：语言、Flow、协议管理、generators**（Task 13–16）。不同组件独立；Flow Task 14 依赖 Task 5；涉及 shared Common/Mapping 的项依赖对应 Wave C。
5. **Wave E：契约文档与集成验收**（Task 17）。只在整个实现完成后集中运行；不在 agent 中途 build/test。

---

## Task 1：Token store 必需 mutation 不可忽略 NoWrite

**Issues:** #324。**Owner:** Security.Foundation.RepositoryTokenStore。**Dependencies:** 无。

**Files:** `src/Schemata.Security.Foundation/Stores/RepositoryTokenStore.cs`；`tests/Schemata.Security.Tests/RepositoryTokenStoreShould.cs`、`RepositoryTokenStoreIntegrationShould.cs`；`docs/documents/security.md`、`authorization.md` 对应 token publication。

**Contract:** 保留 ITokenStore surface、MutationResult 与现有 UoW。安全状态转换成功意味着所有必需 writes 均 Applied 且 commit 完成。

- [ ] 逐方法标注必需 write：redeem、rotation predecessor/member/marker、publish、family invalidation、revocation；prune 与真实幂等 no-op 单独判断，不机械要求删除不存在行。
- [ ] 必需 AddAsync/UpdateAsync 返回非 Applied 时，用现有 SchemataResourceErrors.PreconditionFailed<SchemataToken> 终止，保持原 stamp/status 回滚语义。不要返回 true；不要 catch 后继续。
- [ ] 仅 token publication 的必需 bulk AddRange 替换为既有 per-row AddAsync＋Applied 检查；不更改整个 repository bulk API 或 provider batch 实现。
- [ ] 保持 uncommitted UoW disposal 回滚已 staged writes。lost-CAS 与 advisor veto 不能混作“另一个赢家已成功”。
- [ ] 覆盖 EF/LinqToDB：predecessor veto、第二 successor veto、marker veto、commit failure，观察存储行和返回失败；正常并发只有一个成功；不只 Verify(mock.Update)。

**Acceptance:** veto 不 redeem、不留下部分 successor；正常 rotation 单赢家；失败原子性由现有 transaction 提供。

## Task 2：Repository 事务与 scope/key 语义

**Issues:** #301,#302,#303。**Owner:** Entity.Repository/LinqToDB。**Dependencies:** 无。

**Files:** `src/Schemata.Entity.LinqToDB/{SchemataRepositoryBuilderExtensions,LinqToDbUnitOfWork}.cs`；`src/Schemata.Entity.Repository/RepositoryBase.cs`；对应 provider integration 项目、`docs/documents/repository/providers.md`、`unit-of-work.md`。

- [ ] connection factory 用调用 scope 的 provider 解析用户 context dependencies，保持单 factory contract，不缓存 scoped context/从 root 重解析。
- [ ] 首次 context/transaction 初始化作为一个资源所有权步骤：失败即释放已创建 context，清空内部引用，后续访问重试初始化或明确失败；不能返回半初始化的非事务 context。
- [ ] FindAsync key constant 使用 property.PropertyType；沿现有 OriginalKeyPredicate 规则，不建立 nullable-key 特例 parser。
- [ ] 覆盖两个 scope 的不同 constructor dependencies、初始化异常后再次访问、nullable key lookup、失败后没有非事务 SQL。命令/连接失败由 deterministic provider fixture 注入。

## Task 3：Targets 求值顺序

**Issues:** #289。**Owner:** targets families。**Dependencies:** 无。

**Files:** 三个 `targets/Schemata.{Application,Business,Module}.Targets/Directory.Build.props`、现有 root `Directory.Build.targets`、family variant csproj、Module props/targets；`docs/documents/packages.md`。

- [ ] variant-dependent ItemGroups 移到 variant body 之后求值的单一 family targets import；保持共有默认 props，仅一个 composition owner。
- [ ] 同时覆盖 repository references、Modeling analyzer＋Parlot assets、Modular targets 和 module stamping；不把同样 references 复制到每个 variant。
- [ ] 保留 default-off UseFlow/Insight/Push/Report 等；不把 MSBuild package composition 开关当 runtime feature flag。
- [ ] 验收每个实际 variant 的 evaluated references 与 nupkg dependencies/assets。Use* global override 不参与成功条件；普通 build/pack 必须成立。

## Task 4：RabbitMQ correlation 与 cleanup 归属

**Issues:** #288,#323,#306,#331。**Owner:** Transport.RabbitMq、Event.RabbitMq、Transport.Http。**Dependencies:** 无。

**Files:** `src/Schemata.Event.RabbitMq/{RabbitMqEventOptions.cs,Runtime/RabbitMqConsumerHost.cs}`；`src/Schemata.Transport.RabbitMq/CorrelationTracker.cs`；`src/Schemata.Transport.Http/Extensions/StreamEndpointExtensions.cs`；现有 RabbitMQ/event/transport tests。

- [ ] 删除 Event consumer 的 request-reply completion/failure 分支、unused ReplyTo/tracker 与 RequestTimeoutMs；event correlation 保留为事件 metadata。Messaging reply queue 是唯一 completion owner。
- [ ] timeout/reply/abandon/dispose 只有赢得 pending removal 的路径释放该 pending CTS；竞态失败者不碰已转移资源。用已有 dictionary ownership，不加第二 lock registry。
- [ ] consume advisor failure 纳入既有 outcome/observer-finalization 路径，所有 observers 按既有 audit-last 顺序获得失败；保留原异常，cleanup/observer failure 不吞掉。
- [ ] HTTP stream 的原 failure 与 DisposeAsync failure 通过既有异常归属规则同时保留，不重复 dispose，不改变已开始 NDJSON wire。
- [ ] deterministic race 覆盖 timeout removal vs reply、event 同 request correlation 仍运行 subscribers、consume advisor throws 后 audit、首响应前枚举与 cleanup 双失败；删除只证明废弃 event-reply reflection wiring 的测试。

## Task 5：删除系统 principal，复用无身份的自动生命周期

**Issues:** #286。**Owner:** Scheduling/Flow 及其 bridges。**Dependencies:** Task 1。

**Files:** 删除 `src/Schemata.Scheduling.Skeleton/SchedulingSystemPrincipal.cs`、`src/Schemata.Flow.Foundation/FlowSystemPrincipal.cs`；修改 `{SchedulingAccessPolicy,FlowAccessPolicy}`、现有 domain command/envelope registration、`BackChannelLogoutService`、`SchedulePushHandler`、`FlowEventHandler`、`FlowTimerJob`、IScheduler/JobContext 文档、对应安全与 timer/job tests。

**Contract:** 用户入口负责用户 admission；系统自动推进不携带 framework principal。二者共用一份 handler/transition/persistence body，不能共用“null means trusted”规则。

- [ ] LSP references 枚举所有 static 使用及 public callsites；删除所有 reference-equality checks 和 system principal constructors。
- [ ] 用户 :run/:signal/:cancel/:terminate 等继续通过既有 caller-facing ResourceMethodRequest security wraps；其 principal 只用于该 admission，不自动持久化为 job body 身份。
- [ ] 自动 job firing / event delivery / timer wake 直接进入既有基础 command lifecycle owner，使用当前持久 job key、schedule generation、subscription match、live token/activation 校验；不再把内部 continuation 伪装成带 synthetic principal 的用户资源操作。
- [ ] 如果当前 resource envelope/command registration 把用户策略同时挂在自动 continuation 上，将 caller-specific policy 移到既有资源 envelope，命令生命周期保留可扩展的 domain advisor；handler body 不复制、不加 trusted bool/新 dispatcher。
- [ ] Flow 实例 access/eligibility 从用户 resource method stage执行；内部自动路由保留同一 token/generation admission owner。不存在、过期、stale trigger 仍明确拒绝／无效，不依赖主观“系统身份”。
- [ ] Consumer job/Flow script 需要身份时通过其业务依赖/payload处理；框架不给默认 service account、身份 factory 或 claims convention。
- [ ] 覆盖已启用 auth 下合法 caller 与 denied caller、null caller 用户入口拒绝、自动 timer/event/logout/push 正常推进、stale generation 不推进、job body 未收到 synthetic principal。

## Task 6：Resource/Identity/Authorization 显式 stage 数据与统一可选服务

**Issues:** #181,#144；constants/component consistency。**Owner:** 各组件 Skeleton advisor contract 与 Foundation handler。**Dependencies:** 无；Task 7 依赖本 task。

**Files:** `ResourceMethodOperationHandler`、`Runtime/ResourcePipelineRunner`、`Advisors/IResourceMethod{Request,}Advisor`、method access/entitlement、idempotency suppression types/closures；Identity operation advisors/handlers；Authorization code/token/device-secret advisors及 typed result；相应 Extensions/ServiceCollectionExtensions、tests、docs。

- [ ] 把每个 Set/TryGet payload按 producer/consumer列清单，区分真正 pipeline公共协调与 capability开关/业务 payload/handled response；不删除整个 AdviceContext。
- [ ] Custom method 的 verb、target、request、loaded entity、principal、handled response进入该 stage 的一个显式泛型 execution payload；它是实际 advisor参数与结果 owner，不是 bag 中 carrier。删除 ResourceMethodVerb 和 ctx.TryGet<TResult> handled协议。
- [ ] Create/Update 等已有 request/container/entity 参数补齐显式 handled output，迁移每个 consumer；不能同时保留新 payload 和旧 bag 输出。
- [ ] Identity handled result和 Authorization grant/device-secret数据放入现有 operation/claim/grant execution payload或 AuthorizationResult.Properties 的明确 owner，取消 DeviceSecretIssuance／Reuse bag往返；连续同scope调用不继承上次发行状态。
- [ ] 可选 idempotency仅由 ResourceRegistry/advisor registration选择；删除 Create/Update/MethodIdempotencySuppressed及测试setter，不改为nullable state枚举或 bool。
- [ ] enumerable advisor继续 TryAddEnumerable；multiple selectable implementations通过 keyed/optional services，不搞非generic/generic两份功能。
- [ ] 纯字符串dict/constants移入对应component Constants，保持排序companion与真正behavior类；仅本次触及的新分裂constants归并，不全库改名。
- [ ] 验收多个合作advisor、Handle/Block/Continue、嵌套与连续请求、同 request type不同 custom verbs、未安装可选服务不执行／无隐式fallback。所有测试看输出与I/O，不钉bag键。

## Task 7：幂等性 target、失败结算与明确未知结果

**Issues:** #318,#287。**Owner:** Resource.Foundation；复用 Entity.Repository commit lifecycle。**Dependencies:** Task 1、2、6。

**Files:** `ResourceUpdateIdempotencyPipelineAdvisor`、`ResourceIdempotencyPipelineAdvisor`、`IdempotencyHelper`、`PendingIdempotencyRecord/IdempotencyKind`（仅若既有状态表达不足）、标准 resource handlers、response shaping及custom method invocation；Resource unit/HTTP/gRPC idempotency tests。

- [ ] Update target唯一来自 envelope.Name；其他 verbs核对同一目标来源，payload hash不代替target。caller/tenant partitions保留。
- [ ] 建立一个显式 Resource dispatch execution payload承载reservation lifecycle与mutation completion。复用 Task6 payload和现有 UoW commit/rollback sinks；不能藏在AdviceContext、scoped accessor“最近一次请求”、bool committed或global dictionary。
- [ ] commit证据来自 provider已完成transaction后现有commit owner；rollback证据来自已完成rollback且未commit。未触达mutation的validation/instance denial明确可释放。callback未跑不能自动推断未commit（较早post-commit sink可抛异常）。
- [ ] 成功response准备尽量在拥有的transaction提交前完成可失败mapping；response family/idempotency结算通过一个commit owner顺序协调。未知commit/确认commit后cache失败保留pending或明确unfinalized outcome，不reacquire；不声称cache写与SQL原子。
- [ ] custom methods必须显式报告其当前事务completion through既有执行payload；框架不能根据返回值／exception猜外部sideeffect结果。未提供completion证据的custom mutation失败按未知结果保守处理。
- [ ] 只确认NotWritten/RolledBack时用TryRemoveAsync(key,expectedPending,CancellationToken.None)释放；cleanup failure保留原failure及cleanup failure，不吞异常。
- [ ] waiter以同一等待deadline观察Done／仍Pending／owner已释放；释放后回到TryAdd竞争，只有赢家执行，不把缺slot等到deadline当正确reacquire。
- [ ] timeout/crash/未知SQL commit不新增自动重试。文档公开24h retention不等于known failure poison，unknown protection与普通failure分开。
- [ ] acceptance：A/B不同targets不互串；同target并发一写；precommit失败可重试；veto noWrite释放；response/cache/postcommit sink失败不重复写；取消与unknowncommit保守；另一owner reservation不可删除；HTTP/gRPC一致。

**重要拒绝:** `try { await next(); } catch { TryRemove(pending); }`、commitExtract后翻bool、commit callback cache marker失败仍释放，都不是可接受实现。

## Task 8：Scheduling durable execution的真实结果与参数

**Issues:** #319,#320,#321,#328。**Owner:** Scheduling。**Dependencies:** Task1/5。

**Files:** `Handlers/SchedulingHandlerSupport`、`DefaultTriggerJobHandler`、`DefaultStageJobExecutionResultHandler`、`RunJobHandler`、`Entities/ExecutionStateExtensions`、`OperationMapper`、现有DefaultScheduler生命周期。

- [ ] execution Create非Applied不signal、不arm、不返回成功Operation；使用现有precondition error，不new result flag。
- [ ] :run从持久job复制ArgsJson与既有variables/context，不新造payload表示；restart/context恢复沿原路径。
- [ ] terminal集合统一含已settled Blocked/Skipped；Get/Wait/Cancel、Operation Done使用同一owner，不各自switch复制。
- [ ] one-time entry只在当前generation/entry完成后移除释放；不能清掉已replacement entry、不能由stale callback dispose新timer。
- [ ] 验收trigger veto无row/operation、:run body收到原args、Blocked/Skipped done且wait结束、旧timersettle不删新generation、one-time后entry不累计。

## Task 9：Resource naming、masked mapping 与 Report/Insight结果真实性

**Issues:** #299,#300,#317,#227；#314,#315,#316；#248本轮补充。**Owner:** Common/Mapping、Resource、Insight、Report。**Dependencies:** Task6/7。

- [ ] ResourceIdentifiers.Apply在构造任何predicate之前执行现有ReadAcross admission；MatchSegments拒绝空/whitespace identity，复用现有invalid name/parent error；不改变canonical关联策略。
- [ ] Undelete检查MutationResult；NoWrite不返回恢复detail，复用当前no-content/disposition规则。
- [ ] masked mapping不再完整map真实destination然后只恢复引用。适配器先按当前Map配置生成所需mapped值，再由一个shared mask owner仅把selected leaf/subtree应用到真实目标；whole-field selection允许replacement，descendant selection保留unmasked siblings/refs。不能把所有map配置绕过为按同名propertycopy。
- [ ] 只为mask涉及路径分配projection/暂存，不deep-clone全graph。AutoMapper/Mapster共享MaskTree/assignment，replacement records与collections遵守同一规则；无mask或whole-mask fastpath保留。
- [ ] Insight number分支保留boxedInt64；Limit在达到Take后停止上游MoveNext，Take0不open；PublicModel尊重JsonIgnoreCondition.Never并共用field命名规则。
- [ ] Report在成功headerfinalize后把retention清理移出generationfailurehandler；token取消复用CancelAsync记录已committedcounts，保留OCE传播；不伪造新snapshot state/UID字段。
- [ ] 两个mapperadapter真实集成：mutableSettings、collection、Address.City与Address全选、nestednull、customrenamedmapping、recordreplacement；Report retentionfailure成功header不变／取消counts；Insight长整数与limitcap真实输出。

## Task 10：Tenancy nested lease 与 actor runtime单 owner退休

**Issues:** #330,#290。**Owner:** Tenancy.Foundation／Tenancy.Actor。**Dependencies:** Task2；nested lease先于actor改动。

**Files:** `Services/{CompositeScope,CompositeScopeFactory,TenantCompositeServiceProvider,MemoryCacheTenantProviderCache}`、`ITenantProviderLease`相关实现；`Tenancy.Actor/TenantActorSystem`、`Actor.Foundation`仅通用lifetime/reference seam（确有需要才改）。

- [ ] 每个nestedCreateScope通过当前leaseowner获得自己的同versionlease，并在scope disposal后释放；父scope释放不撤销child使用权，已退休entry不可被当新version重新绑定。
- [ ] provider创建失败、childconstructor失败、scopeDispose异常都exactly-onelease释放；hostonly不需要Tenancy服务。
- [ ] actor retainedref不长持已退休runtime作为重新activationowner。Tenancy ref按tenantidentity/ActorId向Tenancy-owned当前owner解析，mailbox实际引用只在一次turn持有；不能去掉跨租户引用拒绝。
- [ ] runtime退休必须与新turnadmission互斥：无activations且无inflightlease时从当前owner表移除并shutdowndispose；之后retainedref只能解析同一个新owner。复用现有Actor退休/turn生命周期，不复制mailbox，不直接把runtime塞入IServiceProvidercache。
- [ ] 通用ActorFoundation若需“idle并封闭admission”原语，保持tenant-neutral；所有TenantContext/table逻辑在Tenancy.Actor。只一个collector timer。
- [ ] 验收父scope先dispose＋cacheevict后child仍可用；retiredversion不混新；retainedactorref跨idle可重建且仅一个owner；两个同tenant同ActorId并发ref；cross-tenant拒绝；deleted/coldtenant不永久累计runtime。

## Task 11：Redis data/meta真正不相交

**Issues:** #297。**Owner:** Caching.Redis。**Dependencies:** 无。

**Files:** `RedisCacheProvider.cs`、现有Redis tests、`docs/documents/caching/redis.md`。

- [ ] 数据与metadata分别以固定不同namespace前缀＋相同logicalkey导出，标记字符串归现有component Constants。不能只改suffix（任何suffix仍可被合法userkey碰撞）。
- [ ] allGet/Set/Remove/TryAdd/TryReplace/TryRemove/refresh/collectioncommands/Lua KEYS使用同一framingowner；TenantCacheProvider仍只负责逻辑tenantframing，不了解Redismeta。
- [ ] 清洁cutover无旧keydualread/迁移script。消费者已有数据处置自负，修复不授权清空Redis。
- [ ] 实际backendfixture（可用时）覆盖foo与foo:__meta__互相Set/Remove/Refresh不影响、atomicCAS与setcollection都一致；不能只验证KEYS恰是预设字符串。

## Task 12：Authorization/Identity绑定和安全资源生命周期

**Issues:** #294,#295,#313；#244,#247,#268本轮补充；#133locale事实。

- [ ] DCRBearer解析用现有HTTPheaderparser或OrdinalIgnoreCase scheme判断，tokenvalue保持case-sensitive；拒绝malformed/multimechanism，不parallelparser。来源RFC9110§11.1、RFC7592§2。
- [ ] RegisterHandler的plaintextsecret改为singlecalllocal，透过现有response明确赋值；singleton/scoped连续调用均不泄漏，不新增requeststateaccessor。
- [ ] DefaultOpSessionService trusted evidence subject取verifiedhostprincipal，不把callerrequestedsubject重新包装成trusted；现有subjectconflictowner统一拒绝。
- [ ] PairwiseSubjectTranslator用SubjectIdentifierService现有effectiveSubjectTypeowner，不复制optionsfallback。
- [ ] 联系方式changeconfirmation绑定发起的authenticatedaccount，匿名current-contactconfirmation保留现有防枚举shape；tokenpurpose/currentnewcontact由既有UserManager方法验证，不用UID关联替换canonicalref。
- [ ] DPoP locallyownedECDsa在验证所有出口dispose；不disposehostownedkey、不改变Relax/defaultproofchecks。
- [ ] ValidationException保留templatekey＋namedfacts供locale渲染，沿既有SchemataException错误owner；不hardcode英文／第二localeformatter。
- [ ] acceptance：bearer/Bearer相同RAT权限、第二publicclient无前一secret、subjectmismatch拒绝、defaultpairwiseforward/reverse一致、newemailconfirm实际改变发起account、原purposeinvalid失败、cryptoall-exitdispose、locale不丢facts。

## Task 13：Expression typed/value一致性与compile/admission正确性

**Issues:** #298,#304,#305,#307,#308,#325,#326,#327,#332；#258offset补充。

- [ ] CommonEvaluator遵守AndAlso/OrElse/Conditional短路，不预执行不可达branch；复用既有visitor不新增evaluator。
- [ ] AIP stringmatches对null按已有语言contract处理；collectiontype解析共享已存在IEnumerable<T>探测；Logical=false不push复合AND。
- [ ] CELtypedstring+通过既有typedlowering映射string.Concat；nullablecomparison在currentCELspec下明确compileerror或值语义，不能生成nullunbox。执行前读取cel-spec原文和pinnedvectors，不擅自选择新语言语义。
- [ ] macro filteredpredicate不当成bindingname；readonlyobjectkeyedmap member与index共用现有keylookup；timezoneoffset错误转既有CelError，不吞异常作成功。
- [ ] compilefingerprint区分absentalias与literalnone，采用已存在lengthframing/structuralidentity，不新cache或编译器。
- [ ] deterministic边界：false&&throw不throw；nullablefilters；nongenericcollectionsubclass；typed/value字符串/nullable对应；map.member/index；filteredmapreference；nonealias不同tree；unsupportedlogicalresidual；完整CEL向量不新增skip。

## Task 14：Flow自动／直接入口生命周期一致

**Issues:** #309,#310,#311,#312；#270自动throw/catch补充。**Dependencies:** Task5。

- [ ] 自动到达throwcompensation/escalation、catch→eventgateway走既有engine相同dispatcher/transitionowner，不为某节点另建specialhandler。
- [ ] terminaltoken无positionchange也移除其boundarysubscriptions；generation/activation匹配保留。
- [ ] Terminate只改变live tokens，既有Completed/Failed/Cancelled历史不重写。
- [ ] RepositoryFlowEffectRecorder先查本scope_staged，重复RequestId返回同authoritativerecord；确认storedstate与stagedstate合同，不添加全局dedupstore。
- [ ] typedprocedureadmission覆盖每条incoming path（含payloadfree/start/loop），复用现有graphreachability/typeowner，不按缺数据默许valid。
- [ ] 修复#329external-simulator使同RequestId重试响应丢失不会重复effect；测试验证外部effectcount与持久intent，不验证fake自己的配置字段。
- [ ] 执行每个真实consumer-visible路线、multi-entryactivation、unknownexternaloutcome不宣称exactlyonce；只修本轮证据，不顺带补BPMNunsupportedfeatures。

## Task 15：Signature covered input与Unicode一致

**Issues:** #322；#279escapedpath补充。

- [ ] FormUrlEncoded slowpath按Unicode scalar／UTF8连续片段编码，不能逐char拆surrogate；复用平台URI/formencoding机制或已有spanwriter。
- [ ] ASP.NET签名adapter用完整escapedPathBase＋Path与原始query的现有HTTPURIowner；client/server签名base同一request一致，不自行decode/reencode出第二规则。
- [ ] 读取RFC9421实际适用段再改，输入无法保留则现有invalidinput错误，不fallback替换字节。
- [ ] mixedliteralemoji＋percent/plus、mountedpath＋%20/%2F、client/serververify、tamperedcoveredbytes失败；无改算法/keyrotation额外范围。

## Task 16：Generator合法输出与可用诊断

**Issues:** #292,#293。**Owner:** Modeling.Generator。

- [ ] Use与Bases通过GenerateUses同一dedupowner；重复interfaces合法且不复制property，不泛化AST重构。
- [ ] unknownfield/pointer/viewoption在parserboundary走现有locatedSKM002（或既有等价error），不抛出generatorhostcallback；不用catch-all把任何generatorbug标成用户syntaxerror。
- [ ] 输入正常输出通过actualgeneratedconsumer编译；unknownoption给filename/line诊断；重复Use编译且接口行为可消费。不得只assertgeneratedtext/nonempty。

## Task 17：文档cutover与集中验收

**Dependencies:** Task1–16完成。

- [ ] 每task更新其shippeddocs/APIexamples，删除旧bagmarker/systemprincipal指导；不恢复旧issue历史实现。#333只修与纳入任务相关的具体recipe/contract。
- [ ] 整体核对所有namedissues处置、所有调用者/transport/tests与新contract一致；不存在未实现placeholder、兼容alias、tenantleakage、未使用新service。
- [ ] integration owner 统一 build 一次：`dotnet build Schemata.slnx -c Release`；最终验收选择一次 `dotnet test Schemata.slnx -c Release --no-build`。需要按项目隔离结果时，改用各相关 test project 各一次 `--no-build`，不在其后再重复全 solution 同一测试集合。只有实际错误修复后才重新 build 相关产物；不为取日志重复执行同命令。
- [ ] 永久tests仅保留可防真实回归的场景；无额外RED轮次、无每agent中途build/lint/formatter。本计划不把纯wiring/keyshape/countassertions当行为证明。
- [ ] broker/Redis真实适配验收所需backend不可达时记录具体surface未运行；不能用mockecho宣称Lua/settlement已通过。未得到执行授权前不运行任何验收命令。
- [ ] reviewer检查完整diff对计划，特别commit/cache边界、无系统principal、callerdeny、nestedlease/retainedref、NoWrite、两个mapper/provider的语义。
- [ ] taskscopedConventionalCommits、cogcheck与issueevidence更新由已批准执行workflow决定；无push/merge授权。保持generatedchangelog与eng/common不手改。

## 执行交接

推荐 subagent-driven：每task一个implementationowner，独立reviewgate；真正独立任务按wave并行，所有共享contracts先集成。计划本身不启动实现。用户审阅后选择该方式或native实施；未批准前不创建featureworktree、不改source、不关闭issues。
