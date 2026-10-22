# Post-Refactor Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 HEAD `c7b764ae` 的 P0/P1 发现，并收口 #347、#336、#357、#343、#196–#205。

**Architecture:** 每个关注点保留一个 owner。Authorization 的有效 response mode 由 `ResponseModeService` 统一解析；Resource 写入统一走一个 commit-owner 路径；permission 名称统一由 `IPermissionResolver` 产生；Report 执行参数改为显式 payload；Actor 路由由一个泛型 handler 承担；RFC 8288 模型在 HTTP 与 gRPC 之间共享，HTTP 只负责 Link header 投影。

**Tech Stack:** .NET 8/10、ASP.NET Core DI、xUnit v2 + Moq、EF Core/LinqToDB、现有 advisor 与 source generator、HTTP/gRPC 传输。

**Spec:** Issues #351–#360、#347、#336、#357、#343、#196–#205；基线 `docs/superpowers/plans/2026-10-17-urgent-remediation-plan.md` Tasks 17/18/20/21/23/33–36。

## Global Constraints

- 每个技术需求只有一个实现：dispatcher、repository mutation/UoW、idempotency settlement、actor routing、link serializer、permission naming。
- 可选能力通过 `GetService`、`GetRequiredService`、`GetServices`、keyed 或 enumerable advisor 选择；运行时 bool 不承担能力分路。
- advisor 与 context 不携带状态标志；业务数据通过显式 payload 或 DI 服务传递。
- 静态类与静态值不承载每请求状态。
- 租户逻辑只存在于 `Schemata.Tenancy.*`；其他组件读取 `ITenant` trait 之外不感知租户。
- `[ResourceReference]` 保持 string canonical-name 关联，支持单一类型与按 canonical name 解析的多态目标。
- 纯字符串常量字典归 `SchemataConstants` 或对应 component 的 `Constants` 类型。
- 新增包必须由职责边界证明；优先扩展现有包。
- 测试观察业务结果、持久化行、拒绝路径、竞态胜负、scope disposal；descriptor count、mock echo、字符串常量本身不作为唯一断言。
- 同一测试命令只执行一次；需要多个测试命令时先 build 一次，后续使用 `--no-build`。

## Review Focus

1. 省略 `response_mode` 的 code 请求在只允许 `form_post` 时仍按 `form_post` 返回；错误回调携带解析后的有效模式。
2. 启用 implicit/hybrid 后，纯 `token` 或 `id_token` 请求不要求 `code_challenge`。
3. 只注册 `Entity.Repository` 的宿主遇到 `[ResourceReference]` 时得到明确的配置错误，不静默跳过校验。
4. 无 `request_id` 的 Create/Update 在 detail 映射失败时不会提交写入。
5. 两个 report 在同一个 scope 内执行时，`Kind`、`Operation`、取消委托不互相覆盖。
6. Link header 与 gRPC metadata 的目标来自同一 `WebLink` 模型；HTTP 与 gRPC 对同一分页状态给出同一关系数据。

---

## 覆盖映射

| Issue | 处理方式 |
|---|---|
| #334、#337、#338、#342、#345、#285、#291、#296、#350 | HEAD 已修复；执行前复核并关闭，不进入实现任务 |
| #351 | Task 1：有效 response mode |
| #352 | Task 2：PKCE 只约束 code flow |
| #107 | Task 2A：显式 implicit/hybrid/none flow |
| #359 | Task 3：缺少 resolver 时明确失败 |
| #360 | Task 4：单一 commit-owner |
| #353 | Task 5：permission resolver |
| #355 | Task 6：Report 显式 payload |
| #354 | Task 7：共享 Actor handler |
| #356 | Task 8：单一 parent 推导 |
| #358 | Task 9：Modular 类型改名 |
| #357 | Task 10：删除无效选项 |
| #347 | Task 11：组合 handled-response |
| #336 | Task 12：per-enlistment eviction |
| #349 | Task 13：Tenancy cache 包装（旧计划 Tasks 17/18/21 复用） |
| #343 | Task 14：hygiene 收口 |
| #196–#205 | Task 15：RFC 8288 跨传输收口 |

---

## Task 1: 有效 response mode 统一解析与捕获

**Issues:** #351  
**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Services/ResponseModeService.cs`
- Modify: `src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeClientAndRedirect.cs`
- Modify: `src/Schemata.Authorization.Foundation/Handlers/AuthorizeHandler.cs`
- Test: `tests/Schemata.Authorization.Tests/ResponseModeServiceShould.cs`
- Test: `tests/Schemata.Authorization.Tests/AuthorizeCallbackFinalizationShould.cs`

**Interfaces:**
```csharp
// ResponseModeService
public static string? ResolveAllowedMode(
    string? explicitMode,
    string? responseType,
    IReadOnlySet<string> allowedModes);
```
返回值是本次请求的有效模式；`null` 表示没有合法编码，调用方在捕获 callback 前以 `invalid_request` 拒绝。

- [ ] **Step 1: 写失败测试**
  在 `AuthorizeCallbackFinalizationShould` 增加 `Code_OmittedMode_WithFormPostOnly_UsesFormPost` 与 `Hybrid_Error_UsesResolvedFragment`。第一个测试构造 `AllowedResponseModes = ["form_post"]`，请求 `response_type=code` 且省略 `response_mode`，断言 `authz.Callback.ResponseMode == "form_post"` 且成功回调使用 `form_post`。第二个测试复用现有 hybrid 错误路径，断言 `OAuthException.ResponseMode == "fragment"`。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AuthorizeCallbackFinalizationShould"`
  Expected: 新测试失败。
- [ ] **Step 3: 实现 `ResolveAllowedMode`**
  `ResponseModeService` 保留现有 `ResolveMode` 作为纯推断；新增 `ResolveAllowedMode`：
  - 显式模式在集合内，返回显式值。
  - 省略模式时先推断 `query`/`fragment`；推断值在集合内则返回。
  - 推断值不在集合内时，按 `form_post`、`fragment`、`query` 顺序选择对当前 `response_type` 合法的允许模式。
  - 集合为空时返回推断值。
  - 没有合法模式时返回 `null`。
- [ ] **Step 4: 修改 advisor 与 handler**
  `AdviceAuthorizeClientAndRedirect` 在验证 redirect 后调用 `ResolveAllowedMode`；`null` 时抛 `OAuthException(invalid_request)` 并设置 `OmitErrorParameters = true`，在捕获 callback 前返回。非 null 时写入 `authz.ResponseMode`，再用该值构造 `AuthorizationCallback`。`AuthorizeHandler` 构造 context 时继续使用 `ResolveMode`，由 advisor 覆盖为允许模式。
- [ ] **Step 5: 运行测试**
  `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ResponseModeServiceShould|FullyQualifiedName~AuthorizeCallbackFinalizationShould|FullyQualifiedName~AdviceAuthorizeResponseTypeShould"`
  Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Authorization.Foundation/Services/ResponseModeService.cs src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeClientAndRedirect.cs src/Schemata.Authorization.Foundation/Handlers/AuthorizeHandler.cs tests/Schemata.Authorization.Tests`
  `git commit -m "fix(Authorization): honor the effective allowed response mode"`

## Task 2: PKCE 只约束 code flow

**Issues:** #352  
**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizePkce.cs`
- Test: `tests/Schemata.Authorization.Tests/AdviceAuthorizePkceShould.cs`

**Interfaces:**
- Consumes: `AuthorizeRequest.ResponseType`, `CodeFlowOptions.RequirePkce`, `CodeFlowOptions.RequirePkceS256`.
- Produces: `RequirePkce` 只对包含 `code` 的组合生效；challenge 存在时仍执行 RFC 7636 语法校验。

- [ ] **Step 1: 写失败测试**
  增加 `[Theory]`：`token`、`id_token`、`code id_token`、`code token`、`code id_token token`。`RequirePkce=true` 且无 `code_challenge` 时，纯 `token`/`id_token` 返回 `Continue`，包含 `code` 的组合抛 `invalid_request`。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AdviceAuthorizePkceShould"`
  Expected: 纯 implicit 用例失败。
- [ ] **Step 3: 实现 response_type 判断**
  在 advisor 开头计算 `var codeFlow = authz.Request?.ResponseType?.Split(' ').Contains(ResponseTypes.Code) == true;`。仅当 `required && codeFlow && string.IsNullOrEmpty(challenge)` 时抛缺失错误。challenge 存在时继续现有 grammar 与 method 校验。
- [ ] **Step 4: 运行测试**
  `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AdviceAuthorizePkceShould|FullyQualifiedName~AdviceCodeExchangePkceShould"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizePkce.cs tests/Schemata.Authorization.Tests/AdviceAuthorizePkceShould.cs`
  `git commit -m "fix(Authorization): scope PKCE to the code response type"`

## Task 2A: 显式 implicit/hybrid/none flow 与 grant identity

**Issues:** #107  
**Files:**
- Modify: `src/Schemata.Authorization.Skeleton/AuthorizationConstants.cs`
- Modify: `src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeClientAndRedirect.cs`
- Modify: `src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeGrantProfile.cs`
- Modify: `src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeAutoApproveSignIn.cs`
- Modify: `src/Schemata.Authorization.Foundation/Handlers/AuthorizeInteractionHandler.cs`
- Modify: `src/Schemata.Authorization.Foundation/Services/AuthorizationGrantContexts.cs`
- Modify: `src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs`
- Create: `src/Schemata.Authorization.Foundation/Features/ImplicitFlowFeature.cs`
- Create: `src/Schemata.Authorization.Foundation/Features/HybridFlowFeature.cs`
- Create: `src/Schemata.Authorization.Foundation/Features/NoneFlowFeature.cs`
- Modify: `src/Schemata.Authorization.Foundation/Extensions/SchemataAuthorizationBuilderExtensions.cs`
- Test: `tests/Schemata.Authorization.Tests/AdviceAuthorizeGrantProfileShould.cs`
- Test: `tests/Schemata.Authorization.Tests/AuthorizationSignInServiceShould.cs`
- Test: `tests/Schemata.Authorization.Integration.Tests/ImplicitAndHybridFlowShould.cs`
- Test: `tests/Schemata.Authorization.Integration.Tests/NoneFlowShould.cs`

**Interfaces:**
```csharp
// AuthorizationConstants
public static class ResponseTypes { public const string None = "none"; }
public static class ResponseTypeGrantTypes
{
    // Requirements includes [None] = [].
}
```
`UseImplicitFlow`, `UseHybridFlow`, `UseNoneFlow` 在 `UseAuthorizationCodeFlow` 之上增加允许的 response type；它们不重复注册 authorize endpoint、PKCE、nonce、response-mode advisor。

- [ ] **Step 1: 写失败测试**
  `ImplicitAndHybridFlowShould` 断言纯 `token` 请求签发 access token，`grant` context 的 `Source == GrantTypes.Implicit`，`SubjectKind == EndUser`；hybrid `code id_token` 同时返回 code 与 id token，`Source == GrantTypes.AuthorizationCode`。`NoneFlowShould` 断言 `response_type=none` 返回 state 与 session state，响应体没有 code/access_token/id_token；`none token` 被拒绝。`AdviceAuthorizeGrantProfileShould` 断言 implicit 带 `openid` 选 OIDC profile，不带 `openid` 选 OAuth profile。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AdviceAuthorizeGrantProfileShould|FullyQualifiedName~AuthorizationSignInServiceShould"`
  Expected: 新用例失败。
- [ ] **Step 3: 增加常量和 flow 注册**
  增加 `ResponseTypes.None` 与 `ResponseTypeGrantTypes.Requirements[None] = []`。新增三个 `IAuthorizationFlowFeature`，只把对应 response type 加入 `AllowedResponseTypes`/`AllowedResponseModes`；builder 扩展方法提供 `UseImplicitFlow`/`UseHybridFlow`/`UseNoneFlow`。三个 flow 的注册依赖 `UseAuthorizationCodeFlow`，在 feature 构造函数或 `ConfigureServices` 中检查 authorize endpoint 已注册，缺失时抛清晰配置异常。
- [ ] **Step 4: 传递真实 grant identity**
  `AdviceAuthorizeClientAndRedirect` 拒绝 `none` 与其他 token 组合。`AdviceAuthorizeAutoApproveSignIn` 与 `AuthorizeInteractionHandler` 根据 response type 选择 `GrantTypes.AuthorizationCode`（含 code）、`GrantTypes.Implicit`（纯 token/id_token）、`null`（none），不再硬编码 authorization_code。`AuthorizationGrantContexts.KindOf` 将 `GrantTypes.Implicit` 归为 `GrantSubjectKinds.EndUser`。
- [ ] **Step 5: none 路径**
  `AuthorizationSignInService` 在 response type 为 `none` 时跳过 code/access token/id token 分支，只保留 state 与 session-state；`none` 与任何其他 response type 组合在授权 advisor 中拒绝。
- [ ] **Step 6: 运行测试**
  `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AdviceAuthorizeGrantProfileShould|FullyQualifiedName~AuthorizationSignInServiceShould"`
  `dotnet build tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Authorization.Integration.Tests/Schemata.Authorization.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ImplicitAndHybridFlowShould|FullyQualifiedName~NoneFlowShould"`
  Expected: PASS。
- [ ] **Step 7: Commit**
  `git add src/Schemata.Authorization.Foundation src/Schemata.Authorization.Skeleton tests/Schemata.Authorization.Tests tests/Schemata.Authorization.Integration.Tests`
  `git commit -m "feat(Authorization): register implicit, hybrid and none response flows"`

## Task 3: `[ResourceReference]` 缺少 resolver 时明确失败

**Issues:** #359  
**Files:**
- Modify: `src/Schemata.Entity.Repository/Advisors/AdviceValidateResourceReferences.cs`
- Modify: `src/Schemata.Entity.Repository/Advisors/AdviceValidateResourceReferenceExistence.cs`
- Test: `tests/Schemata.Entity.Repository.Tests/Advisors/AdviceValidateResourceReferencesShould.cs`
- Test: `tests/Schemata.Entity.Repository.Tests/Advisors/AdviceValidateResourceReferenceExistenceShould.cs`

**Interfaces:**
```csharp
internal static IResourceTypeResolver RequireResolver(AdviceContext ctx, Type entity);
```
两个 advisor 共用一个 resolver 解析 helper。

- [ ] **Step 1: 写失败测试**
  `AdviceValidateResourceReferencesShould` 增加 `Throws_WhenReferenceDeclared_AndResolverMissing`：实体声明 typed 与 polymorphic `[ResourceReference]`，容器不注册 `IResourceTypeResolver`，断言 `InvalidOperationException` 包含实体名与 resolver 类型。增加 `Continues_WhenEntityHasNoReferenceAttributes_AndResolverMissing`。`Existence` 测试保持现有抛错，改为走同一 helper。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Entity.Repository.Tests/Schemata.Entity.Repository.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Entity.Repository.Tests/Schemata.Entity.Repository.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AdviceValidateResourceReferencesShould|FullyQualifiedName~AdviceValidateResourceReferenceExistenceShould"`
  Expected: 新测试失败。
- [ ] **Step 3: 实现统一 resolver 策略**
  `AdviceValidateResourceReferences.Validate` 在 `references.Count > 0` 且 `GetService<IResourceTypeResolver>()` 为 null 时调用 `RequireResolver` 抛配置错误。没有 reference 属性时继续。`AdviceValidateResourceReferenceExistence` 复用同一 helper，删除自己的重复异常文本。
- [ ] **Step 4: 运行测试**
  同 Step 2 命令。Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Entity.Repository/Advisors tests/Schemata.Entity.Repository.Tests/Advisors`
  `git commit -m "fix(Entity): require the resource type resolver for declared references"`

## Task 4: Create/Update 统一 commit-owner 路径

**Issues:** #360  
**Files:**
- Modify: `src/Schemata.Resource.Foundation/ResourceOperationHandler.cs`
- Modify: `src/Schemata.Resource.Foundation/ResourceOperationHandler.Create.cs`
- Modify: `src/Schemata.Resource.Foundation/ResourceOperationHandler.Update.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceOperationHandlerCreateShould.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceOperationHandlerUpdateShould.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceMutationNoWriteShould.cs`

**Interfaces:**
```csharp
private async Task<TResponse> ExecuteOwnedAsync<TResponse>(
    ResourceDispatchExecution?          execution,
    Func<IUnitOfWork, Task<TResponse>>  mutate,
    CancellationToken                   ct);
```
`execution` 只控制 idempotency marks 与 sink 注册；事务路径对所有请求一致。

- [ ] **Step 1: 写失败测试**
  新增 `Create_WhenDetailMappingThrows_DoesNotCommit` 与 `Update_WhenDetailMappingThrows_DoesNotCommit`。测试使用 mock `ISimpleMapper` 在 `Map<TEntity,TDetail>` 时抛异常，分别以 `execution = null` 和 `execution = new ResourceDispatchExecution()` 调用；断言 mutation 的 commit 未执行、返回异常、dispatched execution 未标记 committed。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ResourceOperationHandlerCreateShould|FullyQualifiedName~ResourceOperationHandlerUpdateShould"`
  Expected: 无 execution 的映射失败用例失败。
- [ ] **Step 3: 改造 `ExecuteOwnedAsync`**
  `execution` 为 null 时不注册 commit/rollback sinks，也不调用 `MarkReached`/`MarkCommitted`；非 null 时保持现有 sink。事务始终由该方法 begin/commit。
- [ ] **Step 4: 删除双路径**
  `CreateCoreAsync`/`UpdateCoreAsync` 删除 `if (execution is null)` 分支，统一调用 `ExecuteOwnedAsync`。`MutateCreateAsync`/`MutateUpdateAsync`/`MutateMissingAsync` 的 `execution` 参数改为 nullable，`MarkNotWritten` 仅在非 null 时调用。detail 映射保持在 commit 前。
- [ ] **Step 5: 运行测试**
  同 Step 2 命令，并运行 `ResourceMutationNoWriteShould`。Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Resource.Foundation/ResourceOperationHandler*.cs tests/Schemata.Resource.Tests`
  `git commit -m "fix(Resource): use one commit owner for identified and unidentified writes"`

## Task 5: instance permission 统一走 `IPermissionResolver`

**Issues:** #353  
**Files:**
- Modify: `src/Schemata.Resource.Foundation/Advisors/AuthorizeHelper.cs`
- Modify: `src/Schemata.Resource.Foundation/Advisors/ResourceAccessStage.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceMissingStageSeparationShould.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceCreateAccessPipelineShould.cs`

**Interfaces:**
```csharp
AuthorizeHelper.EnsureAsync(..., IPermissionResolver resolver, ...);
AuthorizeHelper.FinalizeMissingAsync(..., IPermissionResolver resolver, ...);
```
`IPermissionResolver` 从当前 scope 解析。

- [ ] **Step 1: 写失败测试**
  在 `ResourceMissingStageSeparationShould` 注册自定义 `IPermissionResolver`，返回 `custom.permission`，断言 `PermissionDeniedException` 的 `ErrorInfoDetail.Metadata["permission"]` 或 description 包含 `custom.permission`。现有默认 resolver 用例断言 `order-item.create`。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ResourceMissingStageSeparationShould|FullyQualifiedName~ResourceCreateAccessPipelineShould"`
  Expected: 自定义 resolver 用例失败。
- [ ] **Step 3: 注入 resolver**
  `AuthorizeHelper.EnsureAsync` 已有 `AdviceContext`，从 `ctx.ServiceProvider.GetRequiredService<IPermissionResolver>()` 取 resolver。`EnsureCoreAsync` 与 `FinalizeMissingAsync` 接受 resolver 参数，删除 `Singular.Camelize()` 拼接。
- [ ] **Step 4: 更新 `ResourceAccessStage`**
  `FinalizeMissingAsync` 解析 `IPermissionResolver` 并传给 helper。
- [ ] **Step 5: 运行测试**
  同 Step 2 命令。Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Resource.Foundation/Advisors/AuthorizeHelper.cs src/Schemata.Resource.Foundation/Advisors/ResourceAccessStage.cs tests/Schemata.Resource.Tests`
  `git commit -m "fix(Resource): resolve instance denial permissions through IPermissionResolver"`

## Task 6: Report 执行参数改为显式 payload

**Issues:** #355  
**Files:**
- Modify: `src/Schemata.Report.Foundation/Commands/RunReportRequest.cs`
- Modify: `src/Schemata.Report.Foundation/Handlers/GenerateHandler.cs`
- Modify: `src/Schemata.Report.Foundation/Jobs/ReportGenerationJob.cs`
- Modify: `src/Schemata.Report.Foundation/Handlers/RunReportHandler.cs`
- Delete: `src/Schemata.Report.Foundation/Runtime/ReportExecutionContext.cs`
- Modify: `src/Schemata.Report.Foundation/Extensions/ServiceCollectionExtensions.cs`
- Test: `tests/Schemata.Report.Tests/ReportGenerationJobShould.cs`
- Test: `tests/Schemata.Report.Tests/ReportMethodHandlerShould.cs`

**Interfaces:**
```csharp
public sealed record RunReportRequest(ReportRequest Request, ClaimsPrincipal? Principal)
    : ICommand<ReportResult>, IRequestPrincipal, IReportScoped
{
    [JsonIgnore] public ReportRunKind Kind { get; init; } = ReportRunKind.ImmediatePersisted;
    [JsonIgnore] public string? Operation { get; init; }
    [JsonIgnore] public Func<CancellationToken, ValueTask<bool>>? IsCancelled { get; init; }
}
```

- [ ] **Step 1: 写失败测试**
  在 `ReportGenerationJobShould` 增加 `TwoRunsInOneScope_KeepTheirOwnKindAndOperation`：同一 scope 构造两个 `RunReportRequest`，分别带 `ImmediatePersisted`/`Scheduled` 与不同 operation，调用 `RunReportHandler`，断言每次观察到的值来自自己的 request。现有 job 测试断言取消委托来自 request。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Report.Tests/Schemata.Report.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Report.Tests/Schemata.Report.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ReportGenerationJobShould|FullyQualifiedName~ReportMethodHandlerShould"`
  Expected: 新测试无法构造显式 payload。
- [ ] **Step 3: 增加 request 字段**
  按上面的接口扩展 `RunReportRequest`；`Kind` 默认 `ImmediatePersisted`，`Operation`/`IsCancelled` 可空。
- [ ] **Step 4: 删除 scoped context**
  `GenerateHandler` 构造 `RunReportRequest` 时写入 `Operation`；`ReportGenerationJob` 写入 `Kind`、`Operation`、`IsCancelled`；`RunReportHandler` 从 `request` 读取。删除 `ReportExecutionContext` 与 `TryAddScoped<ReportExecutionContext>()`。
- [ ] **Step 5: 运行测试**
  同 Step 2 命令。Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Report.Foundation tests/Schemata.Report.Tests`
  `git commit -m "refactor(Report): carry run state on the explicit request payload"`

## Task 7: 一个 Actor 路由 handler

**Issues:** #354  
**Files:**
- Create: `src/Schemata.Actor.Foundation/IActorRouting.cs`
- Create: `src/Schemata.Actor.Foundation/Handlers/ActorSerializingHandler.cs`
- Delete: `src/Schemata.Flow.Actor/Handlers/ActorSerializingHandler.cs`
- Delete: `src/Schemata.Push.Actor/Handlers/ActorSerializingHandler.cs`
- Delete: `src/Schemata.Report.Actor/Handlers/ActorSerializingHandler.cs`
- Modify: `src/Schemata.Flow.Actor/Features/SchemataFlowActorFeature.cs`
- Modify: `src/Schemata.Push.Actor/Features/SchemataPushActorFeature.cs`
- Modify: `src/Schemata.Report.Actor/Features/SchemataReportActorFeature.cs`
- Test: `tests/Schemata.Flow.Actor.Tests/ActorPerInstanceSerializationShould.cs`
- Test: `tests/Schemata.Push.Actor.Tests/PushActorConcurrencyShould.cs`
- Test: `tests/Schemata.Report.Actor.Tests/ReportActorConcurrencyShould.cs`

**Interfaces:**
```csharp
public interface IActorRouting<in TRequest>
{
    ValueTask<ActorId?> ResolveAsync(
        TRequest request,
        IServiceProvider services,
        CancellationToken ct);
}
```
`ActorSerializingHandler<TRequest,TResult>` 捕获 `MessageContext`，调用 routing，`ActorId` 为 null 时直接解析 keyed default handler，否则 `AskAsync`。

- [ ] **Step 1: 写失败测试**
  在 Flow/Push/Report actor 测试中各增加一个断言：替换 `IActorRouting<TRequest>` 后，handler 调用 routing 一次、使用返回的 `ActorId`、不解析 keyed handler；`null` 时解析 keyed handler。现有并发测试保留。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Flow.Actor.Tests/Schemata.Flow.Actor.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Flow.Actor.Tests/Schemata.Flow.Actor.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ActorPerInstanceSerializationShould"`
  Expected: 新测试无法编译。
- [ ] **Step 3: 实现共享 handler 与 routing**
  `ActorSerializingHandler<TRequest,TResult>` 放在 `Schemata.Actor.Foundation`，依赖 `IActorSystem`、`IServiceProvider`、`IActorRouting<TRequest>`。三个 component 各提供一个 routing：
  - Flow: `ActorId("flow", request.ProcessCanonicalName)`。
  - Push: `ActorId("push", request.SubscriptionKey)`。
  - Report: 空 key 返回 null；否则解析 `IReportDefinitionStore`，得到 definition canonical name，返回 `ActorId("report", key)`。
- [ ] **Step 4: 迁移注册**
  三个 feature 的 `services.Replace` 改为注册共享 `ActorSerializingHandler`，同时 `TryAddScoped<IActorRouting<...>, ...>`。删除三份旧 handler。
- [ ] **Step 5: 运行测试**
  分别 build/run 三个 Actor 测试项目，使用 `--no-build`。Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Actor.Foundation src/Schemata.Flow.Actor src/Schemata.Push.Actor src/Schemata.Report.Actor tests/Schemata.Flow.Actor.Tests tests/Schemata.Push.Actor.Tests tests/Schemata.Report.Actor.Tests`
  `git commit -m "refactor(Actor): share one routing serialization handler"`

## Task 8: canonical parent 推导单一实现

**Issues:** #356  
**Files:**
- Modify: `src/Schemata.Common/ResourceNameDescriptor.cs`
- Modify: `src/Schemata.Resource.Foundation/Advisors/ChildParentHelper.cs`
- Modify: `src/Schemata.Resource.Foundation/Advisors/AuthorizeHelper.cs`
- Test: `tests/Schemata.Common.Tests/ResourceNameDescriptorShould.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceMissingStageSeparationShould.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceDetailResponsePipelineAdvisorShould.cs`

**Interfaces:**
```csharp
public string? ParentOf(string? canonicalName);
```
根资源、无 parent 的资源、无效 canonical name 返回 `null`；child 返回去掉 collection 与 leaf 后的 parent。

- [ ] **Step 1: 写失败测试**
  `ResourceNameDescriptorShould` 覆盖 `tenants/acme/widgets/w1` → `tenants/acme`，`tenants/acme` → null，`not/a/canonical` → null，多级 parent 正确。`ResourceDetailResponsePipelineAdvisorShould` 断言 link 使用同一 parent `ResourceNameDescriptor.ParentOf`。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Common.Tests/Schemata.Common.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Common.Tests/Schemata.Common.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ResourceNameDescriptorShould"`
  Expected: 新方法不存在。
- [ ] **Step 3: 实现 `ParentOf`**
  使用 `ParseCanonicalName` 验证模式；`_parentSegments.Length == 0` 返回 null；按 `/` 分段去掉最后两段。
- [ ] **Step 4: 迁移两个 helper**
  `ChildParentHelper.DeriveParent` 直接调用 `ResourceNameDescriptor.ForType<TEntity>().ParentOf`。`AuthorizeHelper.ResolveParent<TEntity>` 删除本地分段逻辑，调用同一方法。
- [ ] **Step 5: 运行测试**
  运行 Common、Resource.Tests 的相关 filter。Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Common/ResourceNameDescriptor.cs src/Schemata.Resource.Foundation/Advisors tests/Schemata.Common.Tests tests/Schemata.Resource.Tests`
  `git commit -m "refactor(Resource): derive canonical parents in one place"`

## Task 9: Modular options extension 类型改名

**Issues:** #358  
**Files:**
- Move/Modify: `src/Schemata.Modular/Extensions/SchemataOptionsExtensions.cs`
- Modify: `src/Schemata.Modular/Extensions/ModuleSelection.cs`
- Test: `tests/Schemata.Modular.Tests/ModuleSelectionShould.cs`

**Interfaces:**
- 新类型：`namespace Schemata.Modular; public static class ModularOptionsExtensions`。
- 方法 `GetModules(this SchemataOptions)` / `SetModules` 保持签名，命名空间改变。

- [ ] **Step 1: 写失败测试**
  在 `ModuleSelectionShould` 添加编译级使用：`using Schemata.Modular;` 后调用 `schemata.GetModules()`，并断言 `typeof(ModularOptionsExtensions).FullName == "Schemata.Modular.ModularOptionsExtensions"`。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Modular.Tests/Schemata.Modular.Tests.csproj -c Release`
  Expected: 类型不存在。
- [ ] **Step 3: 移动类型**
  将文件改名为 `ModularOptionsExtensions.cs`，命名空间改为 `Schemata.Modular`，类名改为 `ModularOptionsExtensions`。更新 `ModuleSelection.cs` 与任何 `using Schemata.Core` 的调用点。
- [ ] **Step 4: 运行测试**
  `dotnet build tests/Schemata.Modular.Tests/Schemata.Modular.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Modular.Tests/Schemata.Modular.Tests.csproj -c Release --no-build`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Modular tests/Schemata.Modular.Tests`
  `git commit -m "refactor(Modular): give module options extensions a unique type identity"`

## Task 10: 删除无效的 `InteractionTokenFormat`

**Issues:** #357  
**Files:**
- Modify: `src/Schemata.Authorization.Foundation/Authentication/SchemataAuthorizationOptions.cs`
- Test: `tests/Schemata.Authorization.Tests/AuthorizationOptionsShould.cs`

**Interfaces:**
- 删除 `InteractionTokenFormat` 属性；interaction/logout token 继续使用持久化 reference row，`InteractionTokenLifetime` 保留。

- [ ] **Step 1: 写失败测试**
  在 `AuthorizationOptionsShould` 增加源码级断言：`typeof(SchemataAuthorizationOptions).GetProperty("InteractionTokenFormat")` 为 null。现有 interaction token 测试断言创建的是 `ReferenceId` + `Payload` 行。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  Expected: 新断言失败。
- [ ] **Step 3: 删除属性与文档引用**
  删除 `InteractionTokenFormat`；搜索 `InteractionTokenFormat` 并更新文档/注释。`InteractionTokenLifetime` 保持。
- [ ] **Step 4: 运行测试**
  `dotnet build tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Authorization.Tests/Schemata.Authorization.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AuthorizationOptionsShould|FullyQualifiedName~AuthorizeHandlerShould|FullyQualifiedName~EndSession"`
  Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Authorization.Foundation/Authentication/SchemataAuthorizationOptions.cs tests/Schemata.Authorization.Tests/AuthorizationOptionsShould.cs docs`
  `git commit -m "refactor(Authorization): remove the unused interaction token format option"`

## Task 11: handled-response payload 组合单一 carrier

**Issues:** #347  
**Files:**
- Modify: `src/Schemata.Resource.Foundation/Advisors/ResourceMethodExecution.cs`
- Modify: `src/Schemata.Identity.Skeleton/IdentityOperationExecution.cs`
- Modify: `src/Schemata.Resource.Foundation/ResourceMethodOperationHandler.cs`
- Modify: `src/Schemata.Identity.Foundation/Handlers/IdentityOperationHandler.cs`
- Modify: `src/Schemata.Identity.Foundation/SchemataSignInManager.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceMethodHandler/ResourceMethodEnvelopeShould.cs`
- Test: `tests/Schemata.Identity.Tests/IdentityOperationExecutionShould.cs`

**Interfaces:**
```csharp
// both execution types
private readonly HandledResponse<TResponse> _output = new();
TResponse? IHandledResponse<TResponse>.Response => _output.Response;
bool IHandledResponse<TResponse>.Handled => _output.Handled;
void IHandledResponse<TResponse>.SetResponse(TResponse response) => _output.SetResponse(response);
internal void ClearResponse() => _output.Response = null;
```

- [ ] **Step 1: 写失败测试**
  `IdentityOperationExecutionShould` 断言两个 execution payload 不再声明公共 `Response`/`Handled`/`SetResponse` 属性（通过反射或接口显式实现），并断言 `IHandledResponse` 行为与 `HandledResponse<T>` 一致。Resource method envelope 测试断言 `ClearResponse()` 后 `Handled == false`。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Resource.Tests/Schemata.Resource.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ResourceMethodEnvelopeShould"`
  Expected: 新测试失败。
- [ ] **Step 3: 组合 carrier**
  两个 payload 各持有一个 `HandledResponse<TResponse>`，显式实现 `IHandledResponse<TResponse>`，增加 `internal ClearResponse()`。删除重复的 `Response`/`Handled`/`SetResponse` 公共成员。
- [ ] **Step 4: 更新调用点**
  `ResourceMethodOperationHandler` 的 `execution.Response = null` 改为 `execution.ClearResponse()`；`IdentityOperationHandler` 与 `SchemataSignInManager` 同样改为 `ClearResponse()`。其他 advisor 通过 `IHandledResponse` 写入。
- [ ] **Step 5: 运行测试**
  运行 Resource.Tests 与 Identity.Tests 的相关 filter。Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Resource.Foundation src/Schemata.Identity.Skeleton src/Schemata.Identity.Foundation tests/Schemata.Resource.Tests tests/Schemata.Identity.Tests`
  `git commit -m "refactor(Advice): compose the shared handled-response carrier"`

## Task 12: commit-scoped eviction option 移出 repository 字段

**Issues:** #336  
**Files:**
- Modify: `src/Schemata.Entity.Repository/RepositoryBase.cs`
- Test: `tests/Schemata.Entity.Cache.Tests/Advisors/AdviceCommittedEvictCacheShould.cs`
- Test: `tests/Schemata.Entity.Repository.Tests/RepositoryCommitShould.cs`

**Interfaces:**
```csharp
private sealed class CommitScope
{
    public bool IgnoreCacheEviction { get; set; }
}
```
每个 enlistment 捕获自己的 `CommitScope`；commit/rollback sink 读取并清理该 scope。

- [ ] **Step 1: 写失败测试**
  增加 `QueryWithIgnoreCacheEviction_DoesNotSuppressLaterMutationCommit`：同一 repository 先执行 `IgnoreCacheEviction` 查询，再执行普通 update；断言 committed advisor 收到 `IgnoreCacheEviction=false`。增加 `MutationWithIgnoreCacheEviction_ReachesOnlyItsCommit`。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Entity.Repository.Tests/Schemata.Entity.Repository.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Entity.Repository.Tests/Schemata.Entity.Repository.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RepositoryCommitShould"`
  Expected: 查询污染后续 commit 的用例失败。
- [ ] **Step 3: 引入 per-enlistment scope**
  `Enlist` 创建 `CommitScope` 并捕获到 commit/rollback sink。`TrackEvictionSuppression` 写入当前 enlistment 的 scope；`DispatchCommittedAsync` 读取该 scope 并传 `MutationOptions`。删除 `RepositoryBase._ignoreCacheEviction` 字段。
- [ ] **Step 4: 运行测试**
  运行 Entity.Repository.Tests 与 Entity.Cache.Tests 的相关 filter。Expected: PASS。
- [ ] **Step 5: Commit**
  `git add src/Schemata.Entity.Repository/RepositoryBase.cs tests/Schemata.Entity.Repository.Tests tests/Schemata.Entity.Cache.Tests`
  `git commit -m "refactor(Entity): scope cache-eviction suppression to one enlistment"`

## Task 13: Tenancy cache 自动包装与 #349 收口

**Issues:** #349  
**Files:**
- Create: `src/Schemata.Tenancy.Caching/TenantCacheProvider.cs`
- Modify: `src/Schemata.Tenancy.Foundation/SchemataTenancyBuilder.cs`
- Modify: `src/Schemata.Tenancy.Caching/TenantCacheBuilder.cs`
- Modify: `src/Schemata.Tenancy.Foundation/Features/SchemataTenancyFeature.cs`
- Test: `tests/Schemata.Tenancy.Caching.Integration.Tests/TenantCacheIsolationShould.cs`
- Test: `tests/Schemata.Tenancy.Tests/TenantCacheBuilderShould.cs`

**Interfaces:**
```csharp
internal sealed class TenantCacheProvider(ICacheProvider host, TenantCacheKeyNormalizer normalizer) : ICacheProvider;
```
host cache 通过 keyed `ICacheProvider` `"host-cache"` 暴露，tenant decorator 不解析自身。

- [ ] **Step 1: 写失败测试**
  `TenantCacheIsolationShould` 断言 tenant A/B 对同一逻辑 key 的读写互不可见，host 读写不带 tenant frame；显式 `UseCache` 的 tenant 仍只使用自己的 backend，不叠加 host decorator。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Tenancy.Caching.Integration.Tests/Schemata.Tenancy.Caching.Integration.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Tenancy.Caching.Integration.Tests/Schemata.Tenancy.Caching.Integration.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~TenantCacheIsolationShould"`
  Expected: 未显式 `UseCache` 的 tenant 读到 host key。
- [ ] **Step 3: 暴露 keyed host cache**
  在 host feature 注册 keyed `ICacheProvider` `"host-cache"`，工厂解析 unkeyed host provider。tenant scope 只在没有 tenant unkeyed `ICacheProvider` 时注册 `TenantCacheProvider`，normalizer 使用 verified tenant uid。
- [ ] **Step 4: 保留 dedicated outlet**
  `TenantCacheBuilder.AddOutlet` 继续注册 tenant unkeyed `ICacheProvider`；自动 decorator 使用 `TryAddScoped`，不覆盖显式 outlet。删除任何重复的 tenant cache 探测/flag。
- [ ] **Step 5: 运行测试**
  运行 Tenancy.Caching 与 Tenancy.Tests 的相关 filter。Expected: PASS。
- [ ] **Step 6: Commit**
  `git add src/Schemata.Tenancy.Caching src/Schemata.Tenancy.Foundation tests/Schemata.Tenancy.Caching.Integration.Tests tests/Schemata.Tenancy.Tests`
  `git commit -m "feat(Tenancy): wrap host cache with the tenant key frame by default"`

## Task 14: #343 hygiene 收口

**Issues:** #343  
**Files:**
- Modify: `src/Schemata.Authorization.Skeleton/Contexts/DiscoveryContext.cs`
- Delete: `src/Schemata.Abstractions/Resource/ExpungeResponse.cs`
- Modify: `src/Schemata.Resource.Foundation/ResourceOperationHandler.List.cs`
- Modify: `src/Schemata.Authorization.Foundation/Binding/OAuthBinderHelpers.cs`
- Modify: `src/Schemata.Authorization.Foundation/Extensions/ServiceCollectionExtensions.cs`
- Modify: `src/Schemata.Authorization.Foundation/Features/SchemataAuthorizationFeature.cs`
- Modify: `src/Schemata.Insight.Foundation/Security/InsightSecurityGate.cs`
- Modify: `src/Schemata.Resource.Http/Runtime/HttpResourceHelper.cs`
- Modify: `src/Schemata.Resource.Grpc/Runtime/GrpcResourceHelper.cs`
- Test: `tests/Schemata.Authorization.Tests/OAuthBinderHelpersShould.cs`
- Test: `tests/Schemata.Resource.Tests/ResourceOperationHandlerListShould.cs`
- Test: `tests/Schemata.Insight.Tests/InsightSecurityGateShould.cs`

**Interfaces:**
- `ResourceRegistration.IsEndpointEnabled(string name)` 统一 HTTP/gRPC endpoint 过滤。
- `OAuthBinderHelpers` 使用 Humanizer `Underscore()`。
- `IOrderCompiler` 缺失时抛 `ValidationException` 的 `INVALID_ORDER_BY`，不抛 500。

- [ ] **Step 1: 写失败测试**
  增加 `OAuthBinderHelpersShould.RoundTripsAcronymsWithHumanizer`；`ResourceOperationHandlerListShould` 在未注册 `IOrderCompiler` 时传 `order_by`，断言 `ValidationException` 与 `INVALID_ORDER_BY`；`InsightSecurityGateShould` 断言 operation 来自 `InsightConstants`。
- [ ] **Step 2: 运行失败测试**
  运行对应测试项目的 filter。Expected: 新测试失败。
- [ ] **Step 3: 删除 dead surface**
  删除 `DiscoveryContext` 五个未读取的 `Supports*` 属性；删除 `ExpungeResponse`。更新 XML doc 引用。
- [ ] **Step 4: 收敛 helper 与错误路径**
  `OAuthBinderHelpers.ToSnakeCase` 改为 `name.Underscore()`；`ResourceOperationHandler.List` 使用 `GetService<IOrderCompiler>()`，缺失时抛 `ValidationException(INVALID_ORDER_BY)`；`InsightSecurityGate.Operation` 使用 `InsightConstants.Operations.Insight`；`HttpResourceHelper`/`GrpcResourceHelper` 调用 `ResourceRegistration.IsEndpointEnabled`；`AddSchemataAuthorizationOptions` 与 `ConfigureEndpoints` 保留一处 issuer 校验。
- [ ] **Step 5: 清理工作区目录**
  确认 `src/Schemata.Caching.Distributed` 与 `src/Schemata.Entity.Owner` 只剩 `FodyWeavers.xsd` 后删除目录；更新 `AGENTS.md` 包计数（若仍写死）。
- [ ] **Step 6: 运行测试**
  运行 Authorization.Tests、Resource.Tests、Insight.Tests 的相关 filter。Expected: PASS。
- [ ] **Step 7: Commit**
  `git add src tests docs/AGENTS.md`
  `git commit -m "chore: remove dead surfaces and align helper/error conventions"`

## Task 15: RFC 8288 跨传输收口

**Issues:** #196、#197、#198、#200、#201、#202、#203、#204、#205  
**Files:**
- Move: `src/Schemata.Transport.Http/Linking/WebLink.cs` → `src/Schemata.Common/Linking/WebLink.cs`
- Modify: all HTTP link contributors/`LinkHeader`/`LinkHeaderParser` to use `Schemata.Common.Linking.WebLink`
- Create: `src/Schemata.Transport.Grpc/Linking/IGrpcLinkContributor.cs`
- Create: `src/Schemata.Transport.Grpc/Linking/GrpcLinkContext.cs`
- Create: `src/Schemata.Transport.Grpc/Linking/GrpcLinkMetadataInterceptor.cs`
- Create: `src/Schemata.Transport.Grpc/Linking/ResourcePaginationLinkContributor.cs`
- Modify: `src/Schemata.Resource.Http/Linking/ResourceLinkContributor.cs`
- Modify: `src/Schemata.Push.Http/Linking/PushLinkContributor.cs`
- Modify: `src/Schemata.Flow.Http/Linking/FlowLinkContributor.cs`
- Test: `tests/Schemata.Transport.Http.Tests/LinkHeaderShould.cs`
- Test: `tests/Schemata.Transport.Grpc.Tests/GrpcLinkMetadataShould.cs`
- Test: component `LinkHeaderShould` tests under `tests/Schemata.*.Integration.Tests`

**Interfaces:**
```csharp
public interface IGrpcLinkContributor
{
    IEnumerable<WebLink> GetLinks(GrpcLinkContext context);
}
public sealed record GrpcLinkContext(ServerCallContext Call, object? Response, ResourceRegistration? Resource);
```
gRPC response trailer 写入 `link` metadata，值复用 `LinkHeader.Serialize` 的 link-value 语法；分页目标的 `page_token` 作为 target attribute。

- [ ] **Step 1: 写失败测试**
  `GrpcLinkMetadataShould.PaginatedResponse_CarriesNextLinkMetadata` 调用资源 gRPC List 第一页，断言 trailer `link` 含 `rel="next"` 与 `page_token` 属性，第二页重新请求成功。HTTP 测试断言同一 `next_page_token` 的 Link header 与 gRPC metadata 解析出相同的 rel/token 属性。
- [ ] **Step 2: 运行失败测试**
  `dotnet build tests/Schemata.Transport.Grpc.Tests/Schemata.Transport.Grpc.Tests.csproj -c Release`
  `dotnet test tests/Schemata.Transport.Grpc.Tests/Schemata.Transport.Grpc.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~GrpcLinkMetadataShould"`
  Expected: 新测试失败。
- [ ] **Step 3: 移动 `WebLink` 到 Common**
  将 record 移到 `Schemata.Common/Linking`，namespace `Schemata.Common.Linking`；HTTP 项目添加 `using`。`LinkHeader`/parser/filter 留在 HTTP。
- [ ] **Step 4: 添加 gRPC 投影**
  `IGrpcLinkContributor`/`GrpcLinkContext`/interceptor 收集 contributor 并写 trailer。`ResourcePaginationLinkContributor` 从响应反射 `NextPageToken`，生成 `rel=next`，target 为当前 gRPC method，`page_token` 为扩展属性。
- [ ] **Step 5: 修复 HTTP 硬编码**
  `PushLinkContributor` 使用 `ResourceLinkTargets` 解析订阅集合路径；`FlowLinkContributor` 的 definitions 路径移入 `FlowConstants`；`ResourceLinkContributor` 的 `page_token`/`skip` 使用 `SchemataConstants` 或 `ResourceConstants`。
- [ ] **Step 6: 验证并关闭 issues**
  运行 `tests/Schemata.Transport.Http.Tests` 与各组件的 `LinkHeaderShould`；逐项核对 #197/#198/#200/#201/#202/#203/#204/#205 的 acceptance，满足后关闭。`#196` 在 HTTP/gRPC 同一关系数据测试通过后关闭。
- [ ] **Step 7: Commit**
  `git add src tests`
  `git commit -m "feat(Transport): project RFC 8288 links across HTTP and gRPC"`

---

## 依赖与执行顺序

1. Task 1、2、2A、3、4（P0 与 #107 前置）
2. Task 5、6、7、8、9、10（P1 与 #357）
3. Task 11、12、13、14（#347/#336/#349/#343）
4. Task 15（RFC 8288 收口）
5. 复核并关闭 HEAD 已修复项 #334/#337/#338/#342/#345/#285/#291/#296/#350

Task 1 与 Task 2 共享 `AuthorizeContext`/`CodeFlowOptions`，由同一 owner 先完成。Task 4 与 Task 5 共享 `ResourceOperationHandler`，先完成 Task 4 再改 permission。Task 7 与 Task 6 无依赖，可并行。Task 15 依赖 Task 14 的 `ResourceRegistration.IsEndpointEnabled`。

## 集中验收

- `dotnet build Schemata.slnx -c Release --nologo` 一次。
- 各任务列出的 filtered tests 使用 `--no-build` 执行一次；失败只重跑失败用例。
- 检查 `git diff --check` 与 `cog check` 提交范围。
- 确认没有新增包、没有新的启动 guard、没有 advisor/context 状态标志。
- Gitea issue 在对应 acceptance 通过后关闭；未通过项保留 open 并记录失败证据。
