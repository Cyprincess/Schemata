# Post-Review Remediation Implementation Plan（2026-10-20）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 HEAD `c44090aa` 复核出的 P0/P1 缺陷，收口 #351/#345/#349/#196/#198/#201/#343/#360 的残余，并让文档承诺与实现、标准原文一致。

**Architecture:** 每个关注点保留一个 owner：sign-in 结果准备一个 owner（消除 response_mode 漂移）；implicit/hybrid/none 由授权服务统一按 grant 生成 token；Delete/Undelete 复用 Create/Update 的 commit-owner；租户内部 cache key 统一加 frame；RFC 8288 一个 contributor seam 两个 transport projector；后台 job 的 MessageContext 恢复归 dispatcher；调度结算在 schedule 阶段失败时保留可恢复状态；`[ResourceReference]` 的取值分类与存在性目标解析共用一套 helper。

**Tech Stack:** .NET 8/10、ASP.NET Core DI、xUnit v2 + Moq、EF Core/LinqToDB、既有 advisor/generator/RFC 8288 模型、OpenID Connect/OAuth2（RFC 6749/6750/9068/9700/10017、OIDC Core/Discovery/DCR）、SCIM RFC 7643/7644、AIP-121/122/131/132/133/134/135/136/154/158/164/193。

**Spec:** `docs/superpowers/specs/2026-10-20-post-review-remediation-spec.md`（缺陷编号 X-01…X-28 引用该文件）。

## Global Constraints

- 一个技术需求只有一个实现/入口；新问题先复用现有 mechanism（advisor、DI 可选解析、enumerable/keyed、explicit payload、`ResourceNameDescriptor`、`LinkValue` 序列化）。
- 可选能力通过 `GetService`/`GetRequiredService`/`GetServices`、keyed 或 enumerable advisor 选择；禁止 bool/状态位分路；advisor/`AdviceContext` 不带状态标志，业务数据走显式 payload。
- 纯字符串常量字典归 `SchemataConstants` 或对应 component 的 `Constants` 类型；本计划新增 relation 常量、SCIM 常量遵守该规则。
- 组件隔离：Tenancy 逻辑只在 `Schemata.Tenancy.*`；transport 不依赖安全组件；bridge 走独立 bridge 包或既有 `I*Routing`/Skeleton 契约；Report→Insight 是已批准例外。
- 不新增状态静态类/静态值承载每请求状态；不新增未激活的公开能力面（要么 feature 激活 + 文档，要么删除）。
- 测试只观察业务结果/持久化行/拒绝路径/竞态/scope 释放；同一测试命令只跑一次，后续用 `--no-build`；不运行 smoke/spike。
- Commit 用 Conventional Commits；一个 Task 一个 commit；每个 Task 完成后 build + 受影响测试双 TFM。

---

## Task 1: 纯 implicit 响应补发 ID token（已完成）

**Issues:** X-01（新 issue）
**Status:** 已在 `cb7b598e` 落地；`IsUserGrant` 纳入 `GrantTypes.Implicit`，`AuthorizationSignInServiceShould` 覆盖 `id_token` / `id_token token`（Authorization.Tests 1115/1115，双 TFM）。
**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Authentication/SchemataAuthenticationHandler.cs`
- Modify: `src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs`
- Test: `tests/Schemata.Authorization.Tests/ImplicitFlowShould.cs`（新建）

**Interfaces:**
- `SchemataAuthenticationHandler.IsUserGrant(IDictionary<string,string?> items)` 接受 `GrantTypes.Implicit`（implicit/hybrid 的用户在场 grant 能获得 ID token）。
- 不新增方法；grant 集合仍只在该处定义。

- [x] **Step 1:** 新增 implicit 纯响应测试：构造 `response_type=id_token` 与 `id_token token` 的 authorize 结果，断言返回参数含 `id_token`，且 ID token 携带当前会话 sid（落在 `AuthorizationSignInServiceShould`，复用既有 sign-in 夹具）。
- [x] **Step 2:** 运行该测试确认失败（当时 `grant=implicit` 被 `IsUserGrant` 排除）。
- [x] **Step 3:** 在 `IsUserGrant` 的 grant 集合加入 `GrantTypes.Implicit`；无其他分支。
- [x] **Step 4:** 运行 `tests/Schemata.Authorization.Tests`（net8.0/net10.0）确认无回归。
- [x] **Step 5:** Commit `fix(Authorization): issue ID tokens for pure implicit responses` → `cb7b598e`。

**Acceptance:** 纯 implicit 的 `id_token`/`id_token token` 返回 ID token；hybrid 不回归；`token` 非 OIDC profile 不返回 ID token。

---

## Task 2: 交互成功路径交付已校验的有效 response_mode（并消除 sign-in 准备重复）

**Issues:** X-03、X-13；#351、#135（reopened）
**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Handlers/AuthorizeInteractionHandler.cs`
- Modify: `src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs`
- Modify: `src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeAutoApproveSignIn.cs`
- Create: `src/Schemata.Authorization.Foundation/Services/AuthorizationSignInResultFactory.cs`（internal）
- Test: `tests/Schemata.Authorization.Tests/ResponseModeDeliveryShould.cs`（新建）

**Interfaces:**
```csharp
// internal, one owner for "approved authorize → delivery"
public sealed record AuthorizationSignInResult(
    string RedirectUri, IReadOnlyDictionary<string, string?> Parameters, string ResponseMode,
    string? OpSessionId, string? Sid, Dictionary<string, string?> GrantContext);
internal static class AuthorizationSignInResultFactory {
    public static Task<AuthorizationSignInResult> BuildAsync(SignInBuildContext context, CancellationToken ct);
}
```
`SignInBuildContext` 携带已解析的 `authz`（含 `authz.ResponseMode`）、principal、request；不再从 raw payload 重新 `ResolveMode`。

- [ ] **Step 1:** 新增 delivery 测试：`AllowedResponseModes={"form_post"}` + 省略 `response_mode` 的 code 流，走交互 approve 后断言回调由 `form_post` 交付（form body 而非 query），并断言 error 回调同样使用该模式。
- [ ] **Step 2:** 运行确认失败（当前成功路径 `query`）。
- [ ] **Step 3:** 从 `authz.ResponseMode`（authorize 阶段的 `ResolveAllowedMode` 结果）透传到 interaction payload/`AuthorizationSignInService`；删除 `AuthorizeInteractionHandler:144,227`、`AuthorizationSignInService:486` 的 `ResolveMode` 再推断；`OAuthExceptionFilter` 只使用 payload 中的 mode。
- [ ] **Step 4:** 把 auto-approve advisor 与交互 handler 的 consent/sid/claims/properties 构建合并到 `AuthorizationSignInResultFactory`；两个调用点只保留触发与 reauthentication 差异。
- [ ] **Step 5:** 运行 `Schemata.Authorization.Tests` + `Schemata.Authorization.Integration.Tests`。
- [ ] **Step 6:** Commit `fix(Authorization): deliver the validated response mode and share sign-in preparation`。

**Acceptance:** 省略 response_mode 时 delivery 与 `AllowedResponseModes` 一致（含错误回调）；不存在第二处 `ResolveMode` 调用；两个 sign-in 准备路径的 properties 完全相同（有测试断言）。

---

## Task 3: implicit/hybrid/none flow 的授权元数据与端到端测试

**Issues:** X-15；#107
**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Authentication/ImplicitFlowFeature.cs`
- Modify: `src/Schemata.Authorization.Foundation/Authentication/HybridFlowFeature.cs`
- Modify: `src/Schemata.Authorization.Foundation/Authentication/NoneFlowFeature.cs`
- Modify: `src/Schemata.Authorization.Foundation/Advisors/AdviceDiscoveryCodeFlow.cs`
- Test: `tests/Schemata.Authorization.Tests/ImplicitAndHybridFlowShould.cs`、`NoneFlowShould.cs`（新建）

**Interfaces:**
- flow feature 的 `ConfigureServices` 在缺少 authorize endpoint 能力时抛 `InvalidOperationException`（与现有 feature 配置错误风格一致，如缺少 issuer 的校验），而不是静默注册。
- `grant_types_supported` 由已安装 flow feature 的注册表产生：code 流仍广告 `authorization_code`；implicit/hybrid 按其启用的 response types 广告 `implicit`（不广告未启用项）。

- [ ] **Step 1:** 写端到端测试：纯 implicit 访问 token + ID token；hybrid `code id_token`；none 无 token；`none token` 组合被拒绝；`grant_types_supported` 与已安装 flow 一致。
- [ ] **Step 2:** 确认失败（缺 implicit 广告、无 endpoint 依赖校验）。
- [ ] **Step 3:** 实现 flow registry → discovery 贡献；flow feature 校验 authorize endpoint；默认 discovery 永不广告 implicit（D3 决策：opt-in 降级）。
- [ ] **Step 4:** 运行 `Schemata.Authorization.Tests` + `Schemata.Authorization.Integration.Tests`。
- [ ] **Step 5:** Commit `feat(Authorization): advertise and test the explicit implicit, hybrid and none flows`。

**Acceptance:** #107 的 acceptance 全部可测；默认配置的 discovery 不包含 implicit；只有显式 `UseImplicitFlow()`/`UseHybridFlow()` 才广告对应 grant。

---

## Task 4: Delete/Undelete 复用单一 commit owner

**Issues:** X-05；#360、#287、#318
**Files:**
- Modify: `src/Schemata.Resource.Foundation/ResourceOperationHandler.cs`（`ExecuteOwnedAsync` 泛化）
- Modify: `src/Schemata.Resource.Foundation/ResourceOperationHandler.Delete.cs`
- Modify: `src/Schemata.Resource.Foundation/UndeleteHandler.cs`
- Modify: `src/Schemata.Entity.Repository/ResourceMutation.cs`（删除自提交分支或只保留显式传入 UoW 的路径）
- Test: `tests/Schemata.Resource.Tests/ResourceOperationHandlerDeleteShould.cs`、`…UndeleteShould.cs`（扩展）

**Interfaces:**
- `ResourceOperationHandler.ExecuteOwnedAsync<TResult>(Func<IUnitOfWork, CancellationToken, Task<TResult>> mutate, ResourceDispatchExecution? execution, CancellationToken ct)` 成为唯一提交点；`mutate` 内完成 mutation + detail 映射，提交前任何异常都不落库。
- `IResourceMutation.DeleteAsync/…` 的 `IUnitOfWork?` 参数保留（Flow/ Purge 等真实 owner 传值），但 `ResourceOperationHandler` 必须传外层 UoW。

- [ ] **Step 1:** 写测试：detail mapper 抛异常时 Delete 与 Undelete 不得持久化删除/恢复（对 DB 行断言），且错误类型与 Create/Update 同形。
- [ ] **Step 2:** 确认失败（当前已提交）。
- [ ] **Step 3:** 路由 Delete/Undelete 到 `ExecuteOwnedAsync`，映射移入 `mutate`；`ResourceMutation` 仅在显式 UoW 为 null 的调用方保留自身 commit（如 `PurgeJob` 已传 UoW，可删除该分支）。
- [ ] **Step 4:** `grep -rn "CommitAsync" src/Schemata.Resource.Foundation src/Schemata.Entity.Repository` 确认只有 repository/UoW owner 提交。
- [ ] **Step 5:** 运行 `Schemata.Resource.Tests`、`Schemata.Resource.Http/Grpc.Integration.Tests`。
- [ ] **Step 6:** Commit `fix(Resource): own the commit for delete and undelete`，并评论 #360 关闭。

**Acceptance:** 所有标准方法/自定义方法的 commit 在一个 owner；mapper/序列化失败不产生已提交写入。

---

## Task 5: 租户内部 cache key 的 frame（D1 决策后）

**Issues:** X-06；#349、#24（reopened）、#195、#318
**Files:**
- Modify: `src/Schemata.Resource.Foundation/Advisors/PendingIdempotencyKey.cs`
- Modify: `src/Schemata.Resource.Foundation/Advisors/ResourceIdempotencyPipelineAdvisor.cs`
- Modify: `src/Schemata.Tenancy.Caching/TenantCacheBuilder.cs`（keyed `"host-cache"` 仅显式全局）
- Modify: `src/Schemata.Caching.Skeleton/ICacheProvider` 注册扩展（如需要 keyed 解析）
- Test: `tests/Schemata.Tenancy.Caching.Integration.Tests/TenantCacheBridgeShould.cs`、`tests/Schemata.Resource.Tests/IdempotencyTenantFrameShould.cs`

**Interfaces:**
- `PendingIdempotencyKey` 增加 `TenantIdentity`（来自 scope 的 `ITenantContextAccessor` 或 `ITenant` trait；不得读取静态 `TenantContext`），或由 `ICacheKeyNormalizer` 统一加 frame。
- 无 tenant 时保持现有 key 形态（host 全局），保证现有测试与跨租户 host 行兼容。

- [ ] **Step 1:** 写测试：两个租户对同一 canonical name + request_id 的幂等请求不得互相重放；host/global 与 tenant 行隔离；未声明 `UseCache` 的租户不得读到 host 的幂等响应。
- [ ] **Step 2:** 确认失败（当前 key 无 tenant）。
- [ ] **Step 3:** 实现 frame（优先复用 `ICacheKeyNormalizer`/现有 cache key builder，不新增装饰器）；如 D1 选择 keyed host cache，按 `TryAdd`-safe 顺序注册。
- [ ] **Step 4:** 删除 `plans/2026-10-17`/`2026-10-19` 中互相矛盾的表述（在文档 Task 14 一并做）。
- [ ] **Step 5:** 运行 `Schemata.Tenancy.Caching.Integration.Tests` + `Schemata.Resource.Tests`。
- [ ] **Step 6:** Commit `fix(Tenancy): frame tenant-scoped cache keys`，评论 #349。

**Acceptance:** 跨租户无幂等重放；host 全局行为不变；无新增第二种 cache provider 路径。

---

## Task 6: 一个 RFC 8288 contributor seam + 跨传输投影

**Issues:** X-07、X-21；#196、#198、#201、#204、#205、#145、#199
**Files:**
- Modify: `src/Schemata.Transport.Http/Linking/ILinkContributor.cs`、`LinkContext.cs`、`LinkHeaderResultFilter.cs`
- Delete: `src/Schemata.Transport.Grpc/Linking/IGrpcLinkContributor.cs`、`GrpcLinkContext.cs`、`ResourcePaginationLinkContributor.cs`（合入共享契约）
- Modify: `src/Schemata.Transport.Grpc/Linking/GrpcLinkMetadataInterceptor.cs`、`Extensions/ServicesExtensions.cs`
- Modify: 各 component link contributor 注册（Flow/Push/Scheduling/Report/Insight/Authorization/Resource.Http → 共享契约）
- Modify: `src/Schemata.Abstractions/SchemataConstants.cs`（`LinkRelations`）
- Test: `tests/Schemata.Transport.Http.Tests/LinkHeaderShould.cs`、`tests/Schemata.Transport.Grpc.Tests/GrpcLinkMetadataShould.cs`、各 component `LinkHeaderShould`（扩展 gRPC 断言）

**Interfaces:**
```csharp
public interface ILinkContributor {
    ValueTask<IReadOnlyList<WebLink>> ContributeAsync(LinkContext context);
}
```
`LinkContext` 只携带 transport-neutral 的 resource/response/request 状态（与现有 HTTP 侧字段一致，gRPC 投影从 call/response 构造同一 context）；HTTP projector = 现有 `LinkHeaderResultFilter`，gRPC projector = 现有 `GrpcLinkMetadataInterceptor`。

- [ ] **Step 1:** 写测试：同一 component 关系（Flow 的 process→tokens、Report 的 definition→snapshots、Scheduling 的 job→executions）在 HTTP 与 gRPC 产生相同 `WebLink` 集合；删除 Flow 测试中“gRPC 无 link”的断言。
- [ ] **Step 2:** 确认失败（gRPC 只有分页 next）。
- [ ] **Step 3:** 合并到单一 `ILinkContributor`；HTTP/gRPC 各自只做序列化投影；`SchemataConstants.LinkRelations` 提供 self/next/prev/first/last/canonical/related/up，替换 8 个文件的字面量。
- [ ] **Step 4:** 运行 Transport.Http/Grpc.Tests 与 Flow/Push/Scheduling/Report/Insight/Authorization 相关测试。
- [ ] **Step 5:** Commit `refactor(Transport): project one link contributor seam over HTTP and gRPC`。

**Acceptance:** 同一读取在两种 transport 得到同一关系数据；relation 字面量清零；#198/#201 的 not-applicable 条目在原 issue 中改写。

---

## Task 7: 调度结算与后台执行上下文（#345 残余）

**Issues:** X-08、X-09；#345、#188、#263、#264、#20
**Files:**
- Modify: `src/Schemata.Scheduling.Foundation/Settlement/JobSettlement.cs`
- Modify: `src/Schemata.Scheduling.Foundation/JobExecutionDispatcher.cs`
- Modify: `src/Schemata.Scheduling.Foundation/JobExecutionScopes.cs`（如需）
- Modify: `src/Schemata.Report.Foundation/Jobs/ReportGenerationJob.cs`
- Modify: `src/Schemata.Actor.Scheduling/Runtime/ActorReminderJob.cs`
- Modify: `src/Schemata.Flow.Scheduling/Runtime/FlowTimerJob.cs`
- Modify: `src/Schemata.Push.Scheduling/Runtime/PushDispatchJob.cs`
- Modify: `src/Schemata.Scheduling.Foundation/RunJobHandler.cs`（删除不实注释）
- Test: `tests/Schemata.Scheduling.Tests/JobSettlementShould.cs`、`JobExecutionDispatcherShould.cs`

**Interfaces:**
- 后台 dispatch 的 claim/body/settlement scope 统一走 `JobExecutionScopes.Messaging(IMessageExecutionScopeFactory, new MessageContext(execution.MessageContext))`；`Plain` 仅用于 inline operations。
- settlement 在 schedule 阶段失败时不得 settle execution：保留可恢复的 claimed 行（现有 lease/`FailOrphanedRunningAsync` 回收），dispatcher 读取 `ScheduleSettled` 并重试；删除“只 log warning”的分支。

- [ ] **Step 1:** 改/写测试：schedule 阶段永久失败时 execution 保持可恢复、下一 pass 重试成功；one-time job 不会停留在 Active+过期 NextRunTime；后台 job（Report/Actor/Flow/Push）观察到持久化的 MessageContext（tenant/principal）。
- [ ] **Step 2:** 确认失败（当前测试把丢失写成预期）。
- [ ] **Step 3:** 实现 scope 统一与 settlement 顺序；component job 删除各自的 context 恢复代码。
- [ ] **Step 4:** 运行 `Schemata.Scheduling.Tests`、`Schemata.Scheduling.Integration.Tests`、相关 Actor/Flow/Push/Report 测试。
- [ ] **Step 5:** Commit `fix(Scheduling): preserve schedulable state and restore the persisted context`。

**Acceptance:** #345 的 acceptance 由测试正向断言（不再 `Assert.False(ScheduleSettled)` + 丢失）；job 与 execution 在同一可重试边界结算。

---

## Task 8: 移除写入时 `[ResourceReference]` 校验（owner 决策，已完成）

**Issues:** X-30；#373（closed）、#359/#369（因决策关闭/更新）
**Status:** 已在 `fefd7bae` 落地。

**内容（取代原“值分类统一”方案）：** 删除 `AdviceValidateResourceReferences`、`AdviceValidateResourceReferenceExistence`、`ResourceReferenceAttribute.ValidateExistence`、`AddRepository` 注册、相关单测/集成测试与文档；`[ResourceReference]` 保留为声明式 canonical-name 关联标记，框架不做写入时类型/存在性检查；`AdviceUpdateTenant` 排序锚点改为 `AdviceUpdateValidation.DefaultOrder + 10_000_000`。X-29 同步完成：`IFilterRequest`/`QueryableExtensions.ApplyFilter` 及测试删除（#374）。

**Verification:** Release build 0W/0E；Entity.Repository 57/57、Entity.EntityFrameworkCore.Integration 98/98、Resource 316/316、Tenancy 100/100 + Tenancy.Integration 157/157（net8.0/net10.0）。

## Task 9: 组件边界修复（Transport.Grpc→Security、Authorization bridge）

**Issues:** X-12、X-27；#2、#3
**Files:**
- Modify: `src/Schemata.Transport.Grpc/Schemata.Transport.Grpc.csproj`、`Extensions/GrpcSignatureMessageExtensions.cs`（移出或抽象）
- Create（如决策批准）: `src/Schemata.Security.Grpc/`（bridge：gRPC 消息签名适配）
- Modify: `src/Schemata.Authorization.Foundation/Schemata.Authorization.Foundation.csproj`
- Create（如批准）: `src/Schemata.Authorization.Scheduling/`（token cleanup / back-channel logout jobs 的 bridge）
- Modify: `src/Schemata.Authorization.Foundation/Features/SchemataAuthorizationFeature.cs`（transport 依赖移入 `Authorization.Http/Grpc`）
- Test: 对应 bridge/transport 测试

**Interfaces:**
- Transport.Grpc 不再引用 Security.Foundation；签名适配通过 bridge 包在宿主组合时安装。
- Authorization 的 HTTP/调度绑定从 Foundation 移到 bridge/transport 包；Foundation 只保留协议核心。

- [ ] **Step 1:** 用 `grep` 证明依赖边与使用点；决定 bridge 包名与最小公开面（若新增包，按 component taxonomy 命名并加入 `Schemata.slnx`）。
- [ ] **Step 2:** 移动代码/注册；宿主组合测试（HTTP-only、gRPC-only、scheduling-only）验证不引入额外必需包。
- [ ] **Step 3:** build + 相关测试。
- [ ] **Step 4:** Commit `refactor: move transport and scheduling bindings behind bridges`。

**Acceptance:** transport 不依赖安全组件；Authorization Foundation 不再引用 Transport.Http/Scheduling.Skeleton；无循环依赖。

---

## Task 10: SCIM 产品行为（管理性 Active / PATCH / filter / externalId）

**Issues:** X-02、X-04；#123 #124 #125
**Files:**
- Modify: `src/Schemata.Scim/**`（共享协议：PATCH、filter、error）
- Modify: `src/Schemata.Identity.Scim/Services/ScimUserAdapter.cs`、`ScimGroupAdapter.cs`
- Modify: `src/Schemata.Identity.Scim/Controllers/ScimUsersController.cs`、`ScimGroupsController.cs`
- Modify: `src/Schemata.Identity.Skeleton/**`（administrative active 归属 Identity admission）
- Modify: `src/Schemata.Authorization.Identity/IdentitySubjectProvider.cs`
- Test: `tests/Schemata.Identity.Scim.Integration.Tests`、`tests/Schemata.Identity.Tests`

- [ ] **Step 1:** 写测试：PATCH add/remove/replace 原子落库（中途失败回滚）；filter 至少支持 `userName eq`/`displayName eq`；`externalId` round-trip；`active=false` 阻止 code/refresh/rotate 且不清空 lockout；reactivation 不清空 lockout。
- [ ] **Step 2:** 确认失败。
- [ ] **Step 3:** 在共享 `Schemata.Scim` 实现协议层，Identity 适配层只做映射与 admission；`active` 落到 Identity admission 的独立状态（不复用 `LockoutEnd`）。
- [ ] **Step 4:** 运行两组测试双 TFM。
- [ ] **Step 5:** Commit `feat(SCIM): complete atomic patch, filters and administrative active`。

**Acceptance:** #123/#124/#125 的 acceptance 可测；SCIM surface matrix 更新；无第二套协议层。

---

## Task 11: Identity.Scim 组合改 DI 解析

**Issues:** X-14；#125
**Files:**
- Modify: `src/Schemata.Identity.Scim/Features/SchemataIdentityScimFeature.cs`
- Modify: `src/Schemata.Identity.Scim/Controllers/ScimGroupsController.cs`（能力 seam）

- [ ] **Step 1:** 用 `IScimGroupCapability`（或既有 `IPermissionResolver`/role manager 契约）在 endpoint 映射时通过 `GetService` 决定 Groups surface；删除 descriptor 探测与 MVC controller 移除。
- [ ] **Step 2:** 测试：未安装 role provisioning 时 Groups endpoint 不出现；安装了则出现；组合顺序无关。
- [ ] **Step 3:** Commit `refactor(Identity.Scim): gate group provisioning through DI`。

---

## Task 12: dead/uninstalled surface 与 hygiene（合并项）

**Issues:** X-16、X-17、X-24、X-28、P3 组（REX-05/08/09/10、RD-07/08/10/11、R-SEC-10、RDS-08/09/10/11/12、L-05、L-06）；closed #192、#66、#145、#181
**Files:** 见各 finding 证据；删除/合并，不新增公共 API。

- [ ] **Step 1:** 删除 `AnonymousGranted`、两个 stale `src/` 目录（`git rm` 仅当被跟踪；当前未被跟踪则直接删除工作区目录）、`Map<T>(object,Type,Type)` 与 object `Each/EachAsync`（连带两个 provider 实现）、`Resource.Foundation` 未用的 `Security.Foundation` 引用（build 验证）。
- [ ] **Step 2:** RFC 9421：按 D5 决策删除未激活面或加 `UseHttpMessageSignatures()` + docs。
- [ ] **Step 3:** `PageToken` 改同步 `ToString/FromString`，HTTP filter 删除 `.GetAwaiter().GetResult()`。
- [ ] **Step 4:** `SchemataResourceBuilder` 的 builder 身份字典改为契约/DI keyed 注册；两个 `WithAuthorization` 语义改名为 `WithResourceAuthorization<TEntity>()` 与 `WithAuthorization()`。
- [ ] **Step 5:** `Report.Http` 改用 `ResourceNameDescriptor.ParentOf`（REX-05）；把 `Schemata.Core.Features.SchemataAuthorizationFeature` 改名（如 `SchemataAuthorizationMiddlewareFeature`）以消除与 `Schemata.Authorization.Foundation.Features.SchemataAuthorizationFeature<...>` 的同名（X-28）。
- [ ] **Step 6:** 修复 RDS-08/09/10/11/12 所列文档引用/计数/标题；`docs/documents/packages.md` 与 `AGENTS.md` 用 `git ls-files` 重新生成一致。
- [ ] **Step 7:** Build + 全量相关测试；Commit `chore: remove dead surfaces and align hygiene`。

**Acceptance:** `grep` 死面清零；build 0 warning；文档表格与 HEAD 一致。

---

## Task 13: 文档合规声明与 OIDC/BPMN/priority 表

**Issues:** X-10、X-11、X-25、X-26、X-21 相关（RDS-01..07）；closed #146、#220、#222、#236、#273、#275、#333
**Files:**
- Modify: `README.md`、`docs/index.md`、`docs/documents/core/built-in-features.md`
- Modify: `docs/documents/authorization.md`、`docs/cookbook/oidc-server.md`、`docs/documents/packages.md`
- Modify: `docs/documents/resource/aip-modeling.md`、`aip-interactions.md`
- Modify: `docs/documents/flow/{overview,bpmn-engine}.md`、`docs/documents/report/generation.md`、`docs/documents/identity.md`
- Modify: `AGENTS.md`

- [ ] **Step 1:** 用既有 claim-level 词汇替换三处 “compliant/complies” 声明（AIP/ODIС/BPMN），链接到 requirement-level 矩阵。
- [ ] **Step 2:** `authorization.md` 增加 6 个 flow 激活方法与 RFC 10017 §7.2 / RFC 9700 §2.1.2 的降级披露；修正表结构（session binding 段落移出表格）。
- [ ] **Step 3:** Cookbook 增加 `Schemata.Authorization.Identity` 包；`authorization.md` 示例同步。
- [ ] **Step 4:** priority 表以 HEAD 为准（Transport.Http 201M、Tenancy 205M），补 Push/Report/Authorization/Identity 行；README/index 链接到唯一表格（D2）。
- [ ] **Step 5:** AIP 矩阵修正（AIP-121 Get/List MUST、AIP-231/233-235 State: Approved、AIP 标题、AIP-155 label）；BPMN “full” 改为 feature matrix 的措辞；按 spec D4 更新 #201 的 definitions 导航措辞（或先加 definitions 过滤 seam）。
- [ ] **Step 6:** Commit `docs: align compliance claims and tables with HEAD`。

**Acceptance:** 文档不再出现与自身矩阵矛盾的 blanket 声明；tutorial 可编译；三处 feature 表与 HEAD 一致。

---

## Task 14: Flow wire request 与 dispatch command 统一

**Issues:** X-18；#368、closed #152
**Files:**
- Modify: `src/Schemata.Flow.Skeleton/Models/{CompleteActivityRequest,CorrelateMessageRequest,ThrowSignalRequest}.cs`
- Modify: `src/Schemata.Flow.Foundation/Commands/*`（删除或合并到 wire 类型）
- Modify: `src/Schemata.Flow.Foundation/{FlowRunner,CompleteActivityHandler}.cs`、`Handlers/DefaultCompleteActivityHandler.cs`
- Modify: `src/Schemata.Flow.Foundation/{Extensions/ServiceCollectionExtensions.cs, FlowAuthorizationRegistration.cs}`
- Delete: `ResourceMethodForwardHandler` 的第二 envelope 路径（若采用 wire-model 单一 envelope）
- Test: `tests/Schemata.Flow.Tests/FlowEntryEquivalenceShould.cs`、`FlowAuthorizationRegistrationShould.cs`、`FlowMethodEnvelopeShould.cs`

**Interfaces:** 每个操作一个 request 类型同时满足 wire 绑定与 dispatcher 命令（Report 的 `GenerateReportRequest` 模式），或保留内部命令但让 `IFlowRunner` 走 wire-model envelope；二者选一后删除另一条路径与重复的安全 wrap 注册。

- [ ] **Step 1:** 选定方向（wire 单一类型 vs 单 envelope），写等价的 handler/authorization 测试。
- [ ] **Step 2:** 合并类型与注册，删除 alias import 与第二套 wrap 注册。
- [ ] **Step 3:** 运行 Flow.Tests / Flow.Integration.Tests / Flow.Actor.Tests 双 TFM。
- [ ] **Step 4:** Commit `refactor(Flow): unify wire requests and dispatch commands`。

**Acceptance:** 每个 Flow 操作只剩一个公开 request 类型与一条 envelope 路径；安全 wrap 只注册一次；#368 关闭。

## Task 15: 合并 Flow.Repository / Scheduling.Repository 到各自 Foundation（已完成）

**Issues:** X-33；#375
**Status:** 已在 `06d7f3a9` 落地；包数 90→88，验证见 commit。
**Files:**
- Move: `src/Schemata.Flow.Repository/{RepositoryFlowEffectRecorder.cs,Extensions/SchemataFlowRepositoryServiceCollectionExtensions.cs}` → `src/Schemata.Flow.Foundation/Repository/`
- Move: `src/Schemata.Scheduling.Repository/{RepositoryJobExecutionStore.cs,Extensions/SchemataSchedulingRepositoryServiceCollectionExtensions.cs}` → `src/Schemata.Scheduling.Foundation/Repository/`
- Delete: 两个 `*.Repository.csproj`；更新 `Schemata.slnx`
- Modify: 所有 `ProjectReference`（含 tests、targets 元包）、namespace/using、`docs/documents/packages.md`、`AGENTS.md`、spec/plan 中的包计数

**Steps:**
- [x] **Step 1:** 移动文件并把命名空间/注册扩展并入 Foundation（namespace 跟随 owning package）；删除两个 csproj 与 slnx 条目。
- [x] **Step 2:** 全局替换 ProjectReference（tests/其他 src），移除对已删包的引用；targets 元包若列出包名同步删除。
- [x] **Step 3:** 更新包计数（90→88）与仓库结构表（packages.md、AGENTS.md、spec/plan）。
- [x] **Step 4:** `dotnet build Schemata.slnx -c Release /p:TreatWarningsAsErrors=true` + Flow/Scheduling 单测和集成测试双 TFM。
- [x] **Step 5:** Commit `refactor!: fold repository adapters into the Flow and Scheduling foundations`。

**Acceptance:** 两个包不再存在（源码/nupkg）；Flow/Scheduling 功能与测试不变；文档包计数 88。

## Task 16: 把 Tenancy 的客户端(UA)状态隔离移出 Schemata.Core（已完成）

**Issues:** X-32；#376（#377 重复）
**Status:** 已在 `c4ba6f18` 落地；认证 scope 路由移入 Tenancy，Core 保留通用 state-pipeline 分支 seam（无 tenancy 词汇）并回归原生认证注册。
**Files:**
- Move: `src/Schemata.Core/State/{AuthenticationSchemeScope,ScopedAuthenticationHandlerProvider,ScopedAuthenticationService}.cs`（认证 scope 路由的类型）→ `src/Schemata.Tenancy.Foundation/State/`
- Retain (generic, no tenancy vocabulary): `src/Schemata.Core/State/{IStatePipelineProvider,IStatePipelineValidator,StatePipelineExtensions}.cs` — the only seam that lets any component replace the native session/antiforgery middleware per request. This exception is recorded in spec X-32: keeping it avoids a second implementation path while keeping tenancy out of Core.
- Modify: `src/Schemata.Core/Features/{SchemataSessionFeature,SchemataAntiforgeryFeature,SchemataAuthenticationFeature}.cs`、`src/Schemata.Core/Extensions/ServiceCollectionExtensions.cs`（认证回归原生注册，pipeline 只保留无 tenancy 词汇的通用装配）
- Modify/Move: `src/Schemata.Tenancy.Foundation/State/*`（state profile 构建、authentication scope、per-profile session/antiforgery/cookie/bearer 注册改为自担替换/插入）
- Test: `tests/Schemata.Tenancy.Integration.Tests/{TenantSessionShould,TenantAntiforgeryShould,TenantCookieShould}.cs`、`tests/Schemata.Core.Tests/Core/AuthenticationFeatureShould.cs`（原生 `IAuthenticationService` 注册且无 facade 描述符）

**Steps:**
- [x] **Step 1:** 写/改测试：无 tenancy 的宿主得到原生 `IAuthenticationService` 与原生 session/antiforgery 中间件；有 tenancy 时 per-profile 隔离测试保持通过。
- [x] **Step 2:** 把状态 profile 选择、auth scope 路由、pipeline 分支移入 Tenancy；Tenancy feature 在自身生命周期内替换/插入所需服务与中间件。
- [x] **Step 3:** Core feature 恢复纯原生注册；`grep -rniE 'tenant|UA|client state|state profile' src/Schemata.Core` 为 0（`ExpressionLanguageProfile` 为表达式语言既有类型，与 tenancy 无关）。
- [x] **Step 4:** 运行 Tenancy 集成测试、Core 测试与全量 build。
- [x] **Step 5:** Commit `refactor(Tenancy)!: own client state-profile isolation instead of Core`。

**Acceptance:** `src/Schemata.Core/State` 不再包含 tenancy/客户端/UA 专属实现（认证 scope 路由已移入 Tenancy；仅保留无 tenancy 词汇的通用 state-pipeline seam，理由见 X-32）；Core 不替换 `IAuthenticationService`（新增测试断言只有一个原生描述符）；Core 源码无 tenant/profile/UA 实现词汇；Tenancy per-profile session/antiforgery/cookie/bearer 测试全绿；无 tenancy 宿主行为与原生 ASP.NET Core 一致。

## Task 17: AppDomainTypeCache 保留、加固与反射收口（已完成）

**Issues:** X-31；#378
**Status:** 已在 `6fa8b7df` 落地；含 `AppDomainTypeCacheShould` 测试；D10 记录于 XML 注释。
**Files:**
- Modify: `src/Schemata.Common/AppDomainTypeCache.cs`（late-loaded 可见性、`GetAllTypes`、`GetProperty(type,name)`/`GetFields`）
- Modify: `src/Schemata.Common/ResourceNameDescriptor.cs`、`src/Schemata.Entity.Repository/Estimation/QueryPlanEstimate.cs`、`src/Schemata.Resource.Http/Linking/ResourceLinkContributor.cs`、`src/Schemata.Report.Foundation/Queries/ReadSnapshotResponse.cs`、`src/Schemata.Insight.Foundation/Materialization/PublicModel.cs`、`src/Schemata.Flow.Skeleton/Models/ProcessDefinition.cs`、`src/Schemata.Core/Json/PolymorphicTypeResolver.cs`
- Test: `tests/Schemata.Common.Tests/AppDomainTypeCacheShould.cs`（新/扩展）、polymorphic JSON 测试

**Steps:**
- [x] **Step 1:** 测试 late-loaded assembly 的 `[Polymorphic]` 类型可被发现（动态加载程序集或用测试夹具模拟）。
- [x] **Step 2:** 增加刷新/lazy 访问器并让 `Types.Values` 消费者统一使用；补 property/field 访问器，收口上述裸反射点。
- [x] **Step 3:** 评估 `GetMethod`/`MakeGenericMethod` 是否需要 method cache；不需要则在 XML 注释记录原因（D10）。
- [x] **Step 4:** 全量 build + Common/Mapping/Entity/Resource/Insight 相关测试。
- [x] **Step 5:** Commit `perf(Common): keep one metadata cache and route reflection through it`。

**Acceptance:** 列出的裸属性/字段反射点全部走 cache；late-loaded 类型可见；缓存仅存类型元数据。

## Task 18: 移除 null-forgiving 强制访问并现代化 C# 写法（已完成）

**Issues:** X-34；#379
**Status:** 已在 `4ed3f1d2` 落地；`src/**` 强制访问为 0，null!/default! 剩余 70 处按 AGENTS.md 契约例外，D9 采用评审规则。
**Files:** `src/**`（按 69 处强制访问分布：Flow.Bpmn、Common/ResourceNameDescriptor、Expressions.Cel、Identity.Scim、Identity.Skeleton、Flow.Foundation、Scheduling.Foundation 等）

**Steps:**
- [x] **Step 1:** 逐个把 `foo!.Bar`/`x![i]` 改为 owner 示例的模式匹配 + early return（`is null`、`is not { ... }`、`is not Type b`、`foo?.Bar != true`），必要时用 `switch` expression。
- [x] **Step 2:** 收口 `GetService<T>()!` 与 `null!`/`default!`（`required`、构造赋值、`[NotNullWhen]`、局部函数；保留的运行时契约在声明处逐条注释）；普通参数位置的 `arg!` 不属于 owner 定义的强制访问规则，未做无收益改写（强制访问清零是硬验收）。
- [x] **Step 3:** 决定 D9 的防回归方式（analyzer/评审规则）并落地；`grep -rnE '[A-Za-z0-9_)\]]!\.' src --include='*.cs'` 清零。
- [x] **Step 4:** 全量 build + 受影响测试双 TFM。
- [x] **Step 5:** Commit `refactor: replace null-forgiving access with pattern matching`。

**Acceptance:** `src/**` 无强制访问 `!`；`GetService()!` 模式清零；模式匹配/switch expression 在改动点使用；有防回归措施。

## 依赖与执行顺序

1. Task 1–3（Authorization 协议正确性）互相独立，可并行；Task 2 依赖 Task 1 的测试基建。
2. Task 4、Task 5 独立；Task 5 需要先落 D1 决策。
3. Task 6 独立；Task 8 依赖 Task 4 的 commit-owner（共享 ResourceMutation 改动面）。
4. Task 7 独立于 4/5/6；Task 9 与全部独立但涉及 csproj/依赖图，最后跑全量 build。
5. Task 10/11 依赖 SCIM 决策；Task 12/13 最后执行，避免文档与代码再次漂移；Task 14 与 Task 6/8 改同属 Resource/Flow envelope 附近，可与 Task 9 并行但需与 Task 6 的 Flow link 测试协调。
6. 重新打开的 #24 / #135 的验收由 Task 5 / Task 2 完成时一并关闭；closed issue 的其余残余由对应 open successor 跟踪（见 spec §11）。
7. Task 15（包合并）与 Task 16（Core state 移出）都是公共 API/包图变更，应尽早执行并在同一轮更新 docs/AGENTS；Task 17 与 Task 18 是可独立验证的横切清理，放在 Task 8/15 之后以避免大面积冲突。

## 集中验收

- `dotnet build Schemata.slnx -c Release /p:TreatWarningsAsErrors=true`：0 warning / 0 error。
- 受影响测试项目 net8.0/net10.0 全绿；CI 的 `build`/`docs`/`analysis` workflow 绿色。
- 本计划每个 Task 的 Acceptance 有对应测试；issue 台账状态与计划一致；`docs/superpowers/notes/2026-10-20/` 的 finding 全部有处置结论。
- 追加验收：包数量 88（Task 15 后）；`src/Schemata.Core/State` 无 tenancy/客户端/UA 实现且 Core 不替换 `IAuthenticationService`（Task 16 后，通用 state-pipeline seam 保留、理由见 spec X-32）；`src/**` 无 null-forgiving 强制访问（Task 18 后）；`[ResourceReference]` 无写入时校验（已完成）。
