# Schemata 复核与整改规格（2026-10-20）

## 1. 基线与证据

- 复核窗口：`00c34ae07..HEAD` 共 93 个提交，重点是 2026-10-19 的 28 个执行提交及其最终状态。
- 基线：`90ff2a5f`；本规格编写期间完成 CI 修复提交序列 `89002972` → `c44090aa` → `8d83609b` → `692bde15` → `a73624f7`，后者为本轮最终 HEAD。
- 证据基础：四个领域的静态审查（`docs/superpowers/notes/2026-10-20/{security,data,execution,docs}-reviewer.md`）、Lead 横切审查（`lead.md`）、以及对 2026-10-19 计划 15 个 Task 的执行核对。文档声明已按 `docs/superpowers/AIP.txt` / `docs/superpowers/OIDC.txt` 指定的标准原文核对（AIP-121/135/148/155/158/162/231、RFC 6749/6750/7662/8252/8288/8414/8707/9068/9449/9700/10017、OIDC Back-Channel/DCR、RFC 7643/7644）。
- 验证边界：本规格与计划阶段未运行 smoke/spike；CI 修复的验证使用 `dotnet build Schemata.slnx -c Release /p:TreatWarningsAsErrors=true`（0 warning / 0 error）与受影响测试项目（`Entity.Repository.Tests`、`Tenancy.Integration.Tests`、`Scheduling.Integration.Tests`、`Flow.Integration.Tests`，net8.0/net10.0 全绿）。未改动的领域不做运行时推断；文中每条结论都有 HEAD 的 `file:line` 证据。
- 交付物：本规格、`2026-10-20-post-review-remediation-plan.md`、Gitea `cyp/Schemata` issue 台账更新，以及已推送的 CI 修复提交。

## 2. 已完成：CI 修复

### 2.1 `build` / `docs` workflow（用户指定的 job 118435010264）

1. 第一个错误是 `AdvicePipelineGenerator.cs:15` 的 `RS2008`：generator 项目声明了分析器规则 `SKA001`，但没有 `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md`（`Schemata.Modeling.Generator` 有）。`Directory.Build.props` 对 `generators/**` 打开 `EnforceExtendedAnalyzerRules`，Arcade `-ci` 又把 warning 提升为 error，因此该 warning 直接失败。
2. 修复该错误后，同一构建暴露出被掩盖的 62 个 warnings-as-errors：`Schemata.Resource.Foundation/Advisors` 的 `CS1712`/`CS1574`（由 `10122728` 引入 `TDetail` 参数但未更新 XML 文档）、`Schemata.Flow.Repository` 的 `CS1574`、`Schemata.Resource.*` 测试项目的 nullable 警告、`Schemata.Tenancy.Integration.Tests` 的 `xUnit2029`/`xUnit2031`。
3. 修复方式：为 Advice generator 增加 release tracking 元数据；补齐 XML 文档的 typeparam/cref；在测试中修正 nullable 解引用断言与 xUnit 断言重载。提交 `89002972`。`docs` workflow 与 `build` workflow 同源，一并修复。

### 2.2 `analysis` workflow（job 118435011012）

`Schemata.Tenancy.Integration.Tests` 的 3 个 job/reminder/scheduling 测试失败：

`System.InvalidOperationException : TenantJobExecution requires IResourceTypeResolver for ResourceReferenceAttribute.`

根因：`35d57331`（#359 的修复）对**任何** `[ResourceReference]` 都无条件要求 `IResourceTypeResolver`，包括属性上已经声明了目标类型的 typed reference。框架内部实体（`SchemataJobExecution.Job → SchemataJob`、Flow 的 process 实体）因此让“只装 Entity.Repository + Scheduling”的宿主在持久化时崩溃；而 resolver 的默认实现 `DefaultResourceTypeResolver` 在 `Resource.Foundation`，repository-only 宿主拿不到。

修复 `c44090aa`（保持用户定义的 ref attribute 策略）：

- typed reference（`ResourceReferenceAttribute.Target` 非空）：无 resolver 时用目标类型自己的 `[CanonicalName]` pattern 校验（复用 `ResourceNameDescriptor.ParseCanonicalName`，与 `AdviceApplyChildParent` 同一机制）；有 resolver 时仍要求解析结果可赋值，以支持宿主替换实体类型/pattern。
- polymorphic reference：继续要求 resolver；缺失时在声明属性处抛明确的配置 `InvalidOperationException`（#359 的“不静默跳过”仍然成立）。
- 存在性校验（`ValidateExistence`）：typed reference 直接查声明目标类型的仓储，不再需要 resolver；只有 polymorphic reference 需要。
- 同步更新 `ResourceReferenceAttribute`、advisor XML 文档与 `docs/documents/repository/{ownership,mutation-pipeline}.md`。

验证：`Entity.Repository.Tests` 81/81、`Tenancy.Integration.Tests` 157/157、`Scheduling.Integration.Tests` 31/31、`Flow.Integration.Tests` 174/174（net8.0 与 net10.0）。

### 2.3 CI 修复的续：被前两层掩盖的跨平台/时序问题

RS2008 与 warnings-as-errors 修好后，CI 的三个 workflow 继续暴露了三个测试稳定性问题（均为静态审查范围外、CI 执行才能发现）：

1. **macOS `build` lane（确定性失败）**：`Schemata.Security.Tests.HttpMessageSignerShould.Support_Only_The_Named_Curve_For_The_Ecdsa_Algorithms` 使用 `ECCurve.CreateFromFriendlyName("secP256K1")`，Apple 的 crypto backend 不支持 secp256k1，`ECDsa.Create` 直接抛 `PlatformNotSupportedException`。该测试在 macOS 上 100% 失败（本地 8/8 复现），而 Linux/Windows 通过。修复 `8d83609b`：改用 P-521（各平台都支持的 named curve，且 RFC 9421 未注册对应算法），断言语义不变。
2. **`analysis` lane（负载相关 flake）**：该 lane 以 Debug 并行运行全部测试程序集，5s 上限不可靠；先在 `Schemata.Actor.Scheduling.Tests.ReminderShould`（真实 timer + 5s 轮询）命中，随后又在 `Schemata.Tenancy.Integration.Tests` 命中。修复分两步：`8d83609b` 保留 timer 语义、把该测试上限改为 30s；`a73624f7` 把全仓测试中仅作为**失败延迟上限**的 `WaitAsync` / `CancellationTokenSource` / `ManualResetEventSlim.Wait` 的 5s 统一放宽到 30s（断言、fake clock advance、`Due == 5s` 等把 5s 当业务数据的用法保持原值）。
3. **Windows `build` lane**：`Schemata.Actor.Foundation.Tests` net8.0 失败，从 CI 的 trx 确认唯一失败为 `ActorShutdownShould.Ask_Cancellation_Returns_While_Item_Callback_Blocks_And_Retirement_Waits`，`System.TimeoutException`，耗时 5.16s，落在 `actor.Completion.WaitAsync(5s)`（line 239）；同项目 Linux baseline 5.4s，同一代码在 `8d83609b` 的三 lane `build` 中 Windows 通过，确认是负载相关的 5s 预算 flake 而非确定性缺陷。修复 `692bde15`：把该项目 68 处等待上限提高到 30s（等待只是失败延迟的边界，行为断言不变），并由 `a73624f7` 统一放宽其余测试的 5s 失败预算。

## 3. 上一轮（2026-10-19）计划执行核对

| Task | 声明 | 复核结论 | 证据/残余 |
|---|---|---|---|
| 1 有效 response mode | fixed | **部分** | authorize 阶段已解析并校验，但交互成功路径在 `AuthorizeInteractionHandler`/`AuthorizationSignInService` 重新推断 mode（R-SEC-03，见 X-03） |
| 2 PKCE 仅 code flow | fixed | **已修复** | `AdviceAuthorizePkce.cs:62-72` 按 response_type 门控，测试覆盖纯 token/id_token/hybrid |
| 2A 显式 implicit/hybrid/none | fixed | **部分** | 三个 flow 已注册，但纯 implicit `id_token` 响应漏发 ID token（R-SEC-01，P0）、discovery 未声明 implicit、缺少计划要求的端到端测试（R-SEC-07） |
| 3 resolver 缺失明确失败 | fixed | **已修复（经修正）** | `35d57331` 过宽并破坏 repository-only 宿主，`c44090aa` 收敛为 typed/polymorphic 两种明确策略 |
| 4 Create/Update 统一 commit owner | fixed | **部分** | `#360` 仅覆盖 Create/Update；Delete/Undelete 仍在 `ResourceMutation` 内提交并在提交后映射（RD-01，见 X-05） |
| 5 instance permission 走 resolver | fixed | **已修复** | `AuthorizeHelper` 两条拒绝路径都使用 `IPermissionResolver` |
| 6 Report 显式 payload | fixed | **已修复** | `ReportExecutionContext` 已删除，参数在 `RunReportRequest` 上 |
| 7 单一 Actor routing handler | fixed | **已修复** | 一个 `ActorSerializingHandler` + 各 component 一个 `IActorRouting`；残留三份相同注册块（REX-08，P3） |
| 8 parent 推导单一实现 | fixed | **已修复** | `ResourceNameDescriptor.ParentOf` + `ChildParentHelper`/`AuthorizeHelper`；`Report.Http` 仍自行推导（REX-05，P2） |
| 9 Modular 类型改名 | fixed | **已修复** | 两个 `SchemataOptionsExtensions` 不再同名 |
| 10 删除 InteractionTokenFormat | fixed | **已修复** | 零引用 |
| 11 HandledResponse 单一 carrier | fixed | **已修复** | `HandledResponseRunner` + 各 payload 组合 `IHandledResponse` |
| 12 eviction scope per-enlistment | fixed | **已修复** | `AdviceContext` 只余 `ServiceProvider`，suppression marker 已删除 |
| 13 Tenancy cache 包装 | fixed | **未完成** | 自动 cache 隔离未实现，租户请求会落到未加 frame 的 host provider；且与 10-17 Task 21 决策冲突（RD-02，见 X-06） |
| 14 #343 hygiene | fixed | **部分** | 包计数/未用引用/常量字典已修；`src/Schemata.Caching.Distributed`、`src/Schemata.Entity.Owner` 只剩 `FodyWeavers.xsd`（未被 git 跟踪，工作区残留）；mapper 死重载仍在（RD-07/RD-08） |
| 15 RFC 8288 跨传输 | fixed | **部分** | 模型/序列化/HTTP 投影单实现；gRPC 只有分页 next（RD-03/REX-01），relation 常量仍是字面量（RD-04/REX-07）；#198/#201 另有未达成条目 |

## 4. 缺陷台账（未修复）

### P0

| ID | 缺陷 | 证据 |
|---|---|---|
| X-01 (R-SEC-01) | 纯 implicit `id_token` / `id_token token` 响应不返回 ID token：`IsUserGrant` 只认 authorization_code/refresh_token/token_exchange，而 implicit flow 写入 `grant=implicit` | `SchemataAuthenticationHandler.cs:67-70` → `AuthorizationSignInService.cs:419-421`；`AuthorizationGrantContexts.cs:76-84` |

### P1

| ID | 缺陷 | 证据 | 关联 issue |
|---|---|---|---|
| X-02 (R-SEC-02) | SCIM 的 `active` 复用了 Identity 的 lockout（`LockoutEnd`）作为停用语义；`ISubjectProvider.ValidateAsync` 只查存在性，被停用用户仍可 refresh/rotate token；reactivation 会清空 lockout | `ScimUserAdapter.cs:366-379`、`IdentitySubjectProvider.cs:77-79` | #124 #125 |
| X-03 (R-SEC-03) | 交互成功路径重新推断 response_mode，`AllowedResponseModes` 的校验结果被丢弃，form_post-only 宿主仍可能收到 query 回调 | `AuthorizeInteractionHandler.cs:144,227`、`AuthorizationSignInService.cs:486` | #351 |
| X-04 (R-SEC-05) | SCIM PATCH 返回 501、filter 被拒、`externalId` 从不落库，SCIM 最小合规集的产品行为未完成 | `ScimUsersController.cs:110-113,74`、`ScimGroupsController.cs:108-111`、`ScimResource.cs:19-20` | #123 #124 #125 |
| X-05 (RD-01) | Delete/Undelete 仍在 mutation 内提交，detail 映射在提交之后；映射失败会对已提交删除返回错误（#360 的不变量只覆盖 Create/Update） | `ResourceOperationHandler.Delete.cs:113,125`、`UndeleteHandler.cs:64,72`、`ResourceMutation.cs:132-141` | #360 |
| X-06 (RD-02) | 租户作用域回落到未加 tenant frame 的 host `ICacheProvider`；资源幂等 key 不含 tenant；10-19 Task 13 与 10-17 Task 21 两个计划对 cache 隔离方向相反 | `TenantCacheBridgeShould.cs:44-57`、`ResourceIdempotencyPipelineAdvisor.cs:107`、`PendingIdempotencyKey.cs:41-43` | #349 |
| X-07 (RD-03/REX-01) | 一个 `WebLink` 模型、两个 contributor 契约（HTTP `ILinkContributor` / gRPC `IGrpcLinkContributor`）；gRPC 只注册分页 next，Flow/Push/Scheduling/Report/Insight/Authorization 关系链接永远进不了 gRPC metadata | `Transport.Grpc/Extensions/ServicesExtensions.cs:64-66`、`ResourcePaginationLinkContributor.cs:34`；Flow 测试反向断言了缺失 | #196 #198 #201 #204 #205 |
| X-08 (REX-02) | 后台调度使用 `JobExecutionScopes.Plain`，持久化的 execution `MessageContext` 不恢复；Report/Actor 各自补、Flow/Push 不补，`RunJobHandler` 注释声称的恢复不存在 | `JobExecutionDispatcher.cs:106-108,142`、`JobExecutionScopes.cs:32`、`ReportGenerationJob.cs:37-43` | #345 域 |
| X-09 (REX-03) | schedule 阶段失败后 execution 仍被 settle；`SettlementOutcome.ScheduleSettled` 无生产读者，dispatcher 只记 warning；新测试把“one-time job 保持 Active + 无 pending”写成预期 | `JobSettlement.cs:240-247`、`JobExecutionDispatcher.cs:284-299`、`JobSettlementShould.cs:233-292` | #345 |
| X-10 (RDS-01/RDS-02) | README/`docs/index.md` 的“Google AIP-compliant / complies with OIDC Core 1.0”与项目自己的 Partial/Not implemented 矩阵矛盾；`authorization.md` 的 flow 表缺 6 个 HEAD 公开 flow，未披露 RFC 10017 §7.2（AS MUST NOT 在 authorization response 发 access token）与 RFC 9700 §2.1.2 的冲突 | `README.md:50,117,145,159`、`docs/index.md:124`、`authorization.md:181-202`；RFC 10017 §7.2 原文已核对 | — |
| X-11 (RDS-04) | OIDC cookbook 未添加 `Schemata.Authorization.Identity` 包，`.UseIdentity<SchemataUser>()` 无法编译 | `docs/cookbook/oidc-server.md:23-26,93` vs `SchemataAuthorizationIdentityBuilderExtensions.cs:18` | — |
| X-12 (L-02) | `Schemata.Transport.Grpc` 直接引用并使用 `Schemata.Security.Foundation`（gRPC 签名桥接）；transport 组件不应依赖安全组件 | `Schemata.Transport.Grpc.csproj`、`Extensions/GrpcSignatureMessageExtensions.cs:5` | 新 issue |

### P2

| ID | 缺陷 | 证据 |
|---|---|---|
| X-13 (R-SEC-04) | auto-approve advisor 与交互 handler 各自构建 consent/sid/claims/properties，已发生 mode 漂移 | `AdviceAuthorizeAutoApproveSignIn.cs:85-95`、`AuthorizeInteractionHandler.cs:206-224` |
| X-14 (R-SEC-06) | `Identity.Scim` 通过 `IServiceCollection` 描述符探测 + 移除 MVC controller 决定 Groups 能力，而不是 DI/keyed 解析 | `SchemataIdentityScimFeature.cs:63-89,104-117` |
| X-15 (R-SEC-07) | implicit flow 未写进 discovery 的 `grant_types_supported`；flow feature 不校验 authorize endpoint 已安装；三个 flow 无端到端测试 | `AdviceDiscoveryCodeFlow.cs:43-50` |
| X-16 (R-SEC-08) | RFC 9421 HTTP message signature 能力没有任何 `Use*` feature 激活、无文档，仅单测可达 | `SignatureServiceCollectionExtensions.cs:22-37`；`grep AddHttpMessageSignatures` 仅 1 处 |
| X-17 (R-SEC-09) | `SchemataResourceBuilder` 用 string-keyed 可变字典 + builder 身份对象传递 `ResourceSecurityRegistration`；同一 builder 上 `WithAuthorization` 有两种不同语义 | `SchemataResourceBuilder.cs:30-39,66-69`、`ResourceBuilderSecurityExtensions.cs:19-32` |
| X-18 (REX-04) | Flow 每个操作用两个同名 request 类型（`Flow.Skeleton.Models` vs `Flow.Foundation.Commands`）与两条 envelope 路径，安全 wrap 注册重复 | `Flow.Foundation/CompleteActivityHandler.cs:6`、`FlowRunner.cs:14-16,74`、`FlowAuthorizationRegistration.cs:34-41,49-55` |
| X-19 (REX-05) | `Report.Http` 重新实现 canonical parent 推导，绕过 `ResourceNameDescriptor.ParentOf` | `ReportLinkContributor.cs:49,87-88` |
| X-20 (REX-06) | process→definitions link 指向未过滤列表，无法定位具体 definition；`ListProcessDefinitionsQuery` 无过滤参数 | `FlowLinkContributor.cs:30`、`ListProcessDefinitionsQuery.cs:8` |
| X-21 (RD-04/REX-07) | RFC 8288 relation 常量未进入 Constants 类型，8 个文件散落字面量 | `ResourceLinkContributor.cs:64...`、各 Http contributor |
| ~~X-22 (RD-05)~~ | **已被 owner 决策取代（2026-10-20）**：框架不再做写入时 ref 校验，X-30 删除两个 advisor；`[ResourceReference]` 保持声明式关联元数据 | — |
| ~~X-23 (RD-06)~~ | **已被 owner 决策取代**：`ValidateExistence` 与两个 advisor 删除（X-30），不存在校验目标不一致问题 | — |
| X-24 (RD-09) | `PageToken` 假异步 API 迫使同步 HTTP filter `.GetAwaiter().GetResult()` | `PageToken.cs:77-98`、`ResourceLinkContributor.cs:230,250` |
| X-25 (RDS-03) | 三处 feature priority 表与 HEAD 不一致：Transport.Http 代码 201M vs 文档 410M；README Tenancy 160M vs 代码 205M；缺 Push/Report/Authorization/Identity 等扩展行 | `SchemataTransportHttpFeature.cs:22`、`SchemataTenancyFeature.cs:39`、`README.md:77,96`、`built-in-features.md` |
| X-26 (RDS-06/RDS-07) | “full BPMN 2.0.2 engine” 与自家 Not-supported 矩阵矛盾；AIP-231/233-235 状态误写、AIP-121 的 Get/List MUST 未入矩阵、AIP 标题误用 “Guidance” | `README.md:171`、`aip-modeling.md:67,71,85`、`aip-interactions.md:99-102` |
| X-27 (L-03) | `Authorization.Foundation` 直接引用 `Transport.Http` 并内嵌 Scheduling job；其他 component 用 bridge 包（`Report.Scheduling` 等），Authorization 没有 | `Schemata.Authorization.Foundation.csproj`、`TokenCleanupJob.cs`、`BackChannelLogoutJob.cs` |
| X-28 (L-04) | `Schemata.Core.Features.SchemataAuthorizationFeature`（ASP.NET 中间件）与 `Schemata.Authorization.Foundation.Features.SchemataAuthorizationFeature<...>`（OAuth AS）同名不同职责 | 两个文件同名类型 |

### P3（一致性/卫生，建议合并为一个 hygiene issue）

REX-08 actor 注册块重复；REX-09 BPMN compensation 通知双实现；REX-10 Report inline 限额/输出映射双份；RD-07 两个 stale `src/` 目录；RD-08 `Map<T>(object)`/`Map<T>(object,Type,Type)`/object `Each*` 死面；RD-10 `Resource.Foundation` 未用的 `Security.Foundation` ProjectReference + 两个一行 helper；RD-11 `ClearResponse` 可见性/scoped 状态；R-SEC-10 `AnonymousGranted` 死类型；RDS-08 六处过期引用；RDS-09 包清单 88/90、`.Owner`、`.RabbitMQ`；RDS-10 AGENTS 自相矛盾；RDS-11 DCR “full metadata validation” 措辞；RDS-12 AIP-155 label；L-05 `ResourceIdentifiers` 同名；L-06 测试 resolver 副本（typed-ref 修复后大多不再需要）。

## 5. 需要裁决的决策点

| 决策 | 选项 | 建议 |
|---|---|---|
| D1 Tenancy cache 隔离（X-06；#24 已按此验收重新打开，#364 为实施跟踪） | (a) 按 10-19 Task 13 实现 keyed host cache + `TenantCacheProvider` 自动装饰；(b) 只给框架内部 per-request cache key（如幂等 key）加 tenant frame，宿主显式 `UseCache` 才加 frame | 采用 (b)+keyed `"host-cache"` 仅用于显式全局场景：立刻消除跨租户幂等重放，不引入新的隐式装饰器/第二套缓存路径；随后再评估自动装饰。无论哪种，必须在计划中删除 10-17/10-19 互相矛盾的表述。 |
| D2 Transport.Http priority | 代码 201M vs 文档 410M | 文档改为 201M：Transport.Http 是内建 feature，应位于内建区间（100M–400M）且在 Tenancy（205M）之前，才能让异常处理/JSON trait 包裹租户与认证中间件；410M 属 extension 区间是文档错误。 |
| D3 implicit/hybrid token 与 RFC 10017 §7.2 | (a) 保留 opt-in flow 作为允许的降级选项（类比 RelaxXXX），文档明示规范冲突且默认 discovery 不广告；(b) 限制 `token` 变体不再在 authorization response 发 access token | (a)：用户已确认 Authorization 的降级选项是允许的；但必须文档化并按“降级”命名/披露，不能出现在默认发现文档中。 |
| D4 #201 definitions 导航（X-20） | 给 definitions 列表加 `name`/`version` 过滤 seam，或把该条改成 not applicable | 先改 issue 措辞为 not applicable@HEAD，若后续有 definitions 过滤需求再做统一 list filter。 |
| D5 RFC 9421 surface（X-16） | 增加 `UseHttpMessageSignatures()` feature + docs，或删除 | 在 package 发布前二选一；建议本轮删除未激活面（保持“能力必须通过 feature 组合”），需要时按新能力重提。 |

## 6. Issue 台账处置

- **关闭**（有 HEAD 证据且验收满足）：#285 #291 #334 #336 #337 #338 #342 #347 #350 #352 #353 #354 #355 #356 #357 #358 #359 #360（Create/Update；Delete/Undelete 另立） #197 #200 #202 #203 #204 #205。
- **评论并保留**： #196（gRPC parity/常量）、#198（gRPC metadata + 两条 not-applicable）、#201（definitions 导航）、#343（mapper 死面 + stale dirs）、#345（schedule loss）、#349（cache 隔离 + D1）、#351（成功路径 delivery）、#107（implicit ID token/discovery/tests）、#123/#124/#125（PATCH/filter/externalId/admin active）、#61（未开始）。
- **新建**（本规格第 4 节对应项）：implicit ID token（P0）、SCIM 管理性 Active、有效 response_mode 交付、SCIM PATCH/filter/externalId、Delete/Undelete commit owner、tenant frame cache key、HTTP+gRPC 单一 link contributor、后台 job message context、schedule loss、Transport.Grpc→Security 依赖、Authorization bridge 收敛、Flow request/envelope 统一、`[ResourceReference]` 值分类统一、RFC 9421 面处置、docs 合规声明+OIDC flow+priority 表、OIDC cookbook 包、hygiene 合并项。
- **重新打开**（关闭结论被 HEAD 证据推翻，见 §11）：#24（租户 cache 隔离验收未达成）、#135（成功路径仍绕过已验证 callback 上下文）。
- **closed issue 交叉引用/评论**（不重开，残余由 open successor 跟踪）：#2 #20 #39 #66 #97 #139 #145 #146 #188 #192 #195 #199 #236 #263 #264 #318 #333。
- **追加轮（owner 2026-10-20 反馈）**：新建 #373（移除写入时 ref 校验，已完成关闭）、#374（移除 IFilterRequest/ApplyFilter，已完成关闭）、#375（合并 Flow/Scheduling.Repository 包）、#376（Tenancy 状态隔离移出 Core；#377 为重复已关闭）、#378（AppDomainTypeCache 保留+加固+反射收口）、#379（移除 null-forgiving 强制访问）；#369 因 owner 决策关闭。
- **改写**：#351（`AuthorizationCallback` 已删除）、#359（新策略描述）、#343（明确剩余 mapper/dir 两项）。

## 7. 能力建设路线

1. **本轮收口（P0/P1）**：X-01 → X-02 → X-03 → X-05 → X-06 → X-07 → X-08/X-09 → X-10/X-11 → X-12。目标：协议正确性、租户隔离、持久化一致性、单一 link 投影、文档不再虚假承诺。
2. **SCIM 最小合规集**（#123–#128，Medium）：PATCH/filter/externalId + 管理性 Active + Applications schema + interoperability 证据；共享 `Schemata.Scim` 协议层，不新增第二个协议实现。
3. **OAuth/OIDC 后续能力**（Medium）：CIBA 家族（#36/#108/#109/#114/#118）、JAR 加密与外部 request_uri（#101/#102）、step-up（#115）、浏览器部署 profile 与证据（#110/#112）、历史 password grant（#100，显式 opt-in）。
4. **平台能力**（Medium）：Quota 持久预留/账本（#59/#61）、Rules（#49）、BFF views（#53）。
5. **跨节点**（High，#27）：Orleans actor provider；必须作为 `Schemata.Actor.Orleans` provider 包，经 DI/枚举解析接入现有 actor 抽象，不改变本地单 owner 语义。
6. 每个能力开工前检查：是否真的需要新包（优先扩展现有 component/bridge/provider）；可选性是否由 DI `GetService(s)`/keyed/enumerable 表达；advisor/context 是否无状态标志；是否复用既有 mechanism。

## 8. 非目标

- 不在本轮实现 CIBA/JAR/step-up/quota/Orleans 产品能力（见第 7 节排期）。
- 不做跨组件大规模重构；新增 bridge 包仅在决策 D 明确后。
- 不重写文档站结构；只定点修正 X-10/X-11/X-25/X-26/RDS-08/RDS-09。
- 不提供旧 API 兼容 alias、迁移工具、双写或遥测。

## 9. 验证纪律与完成条件

- 任何实现任务先写/改测试再改代码；只观察业务结果、持久化行、拒绝路径、竞态与 scope 释放，不以 descriptor 数量/mock echo 作为唯一断言。
- 构建以 `dotnet build Schemata.slnx -c Release /p:TreatWarningsAsErrors=true` 为准；受影响测试项目必须 net8.0/net10.0 双 TFM 通过；不运行 smoke/spike。
- 完成条件：P0 全部关闭；P1 全部关闭或经决策显式延期；issue 台账与计划勾选状态一致；`build`/`docs`/`analysis` 三个 CI workflow 绿色（CI 作为最终验证者）。

## 10. 复核统计

- 复核窗口：93 个提交；HEAD 最终状态 88 个 `src` 包、63 个测试项目（Task 15 合并两包后）。
- 四个领域 reviewer 的静态结论：issue 裁决合计（去重后覆盖 58 个 open issue 中的 44 个）——Fixed 28 项次、Partially fixed 15 项次、Still open 14 项次、Obsolete/Duplicate 0。
- closed issue 综合分析：本轮开始时 296 个 closed（当前 API 计数 321，差额为本轮关闭的 25 个）；按主题交叉核对后重新打开 2 个、在 24 个 closed issue 上留下 HEAD 证据评论、并让 12 个新建 issue 显式引用其 closed 前身（明细见 §11）。
- Lead 横切 findings 7 条（L-01…L-07），其中 L-01（typed `[ResourceReference]` 过度要求 resolver）已在 `c44090aa` 修复并作为 CI 失败根因记录。
- 新发现缺陷 28 条（X-01…X-28）+ owner 追加方向 6 条（X-29…X-34）：P0 1 条、P1 11 条、P2 13 条、P3 合并为 hygiene；X-22/X-23 被 X-30 取代。
- Gitea 台账：关闭 32 个已修复/已决定 issue（含 #373/#374/#375/#376/#377/#378/#379/#369），保留并评论 12 个部分修复/残余 issue，新建 19 个 issue（#361–#379，#377 为重复）；当前 open 46 / closed 327。
- CI：`70d7aafb`（owner 反馈轮最终提交）上的 **build（ubuntu/macos/windows 三 lane + publish）、docs（Build Documentation + Pages）、analysis（Cocogitto + SonarQube 含全量测试）三个 workflow 全部 success**；此前的 `a73624f7` 亦为全绿基线。此前各 lane 的失败根因均已定位并修复：RS2008/warnings-as-errors（`89002972`）、typed reference 策略（`c44090aa`）、macOS ECDSA secp256k1（`8d83609b`）、Windows ActorShutdown 5s TimeoutException（`692bde15`）、并行 lane 的 5s 失败预算 flake（`8d83609b`/`a73624f7`）。

## 11. Closed issue 交叉分析（296 → 321）

### 11.1 范围与方法

- 本轮开始时 Gitea `cyp/Schemata` 有 **296 个 closed issue**；通过 tekoki Gitea MCP 全量拉取（7 页、321 条，含本轮关闭的 25 条），按标题/标签/正文与评论做主题交叉核对，重点比对"关闭声称"与 HEAD `a73624f7` 的实际代码。
- 方法：先按主题匹配当前 28 条 finding（X-01…X-28）与 2026-10-19 计划残余，再读取候选 closed issue 的全文与关闭评论，判断其 acceptance 在 HEAD 是否仍成立；对仍成立且无 open successor 的项重开，对有 open successor 的项评论引用（遵循此前 "#152 → #336/#339 不重开避免重复跟踪" 的先例）。

### 11.2 处置结果

| closed issue | 主题 | HEAD 证据 | 处置 |
|---|---|---|---|
| #24 | Tenancy cache/query 按执行身份隔离 | 租户无 outlet 时回落到未加 frame 的 host cache（`TenantCacheBridgeShould.cs:44-57`）；幂等 key 无 tenant（`PendingIdempotencyKey.cs:41-43`） | **重开**；实施跟踪 #364，决策 D1 |
| #135 | 从已验证 response 上下文 finalize callback | 交互成功路径仍用 `ResolveMode` 重新推断（`AuthorizeInteractionHandler.cs:144,227`、`AuthorizationSignInService.cs:486`） | **重开**；实施跟踪 #351 / 计划 Task 2 |
| #97 #139 | discovery 只发布有效能力 / 可选 feature 治理 endpoint | flow feature 只加 response type，不贡献 discovery、不校验 authorize endpoint（`ImplicitFlowFeature.cs:17-24`、`AdviceDiscoveryCodeFlow.cs:43-50`） | 评论；#107 跟踪 |
| #195 #318 #287 | AIP-155 idempotency / commit/reservation | 幂等 key 无 tenant；Delete/Undelete 仍在 mutation 内提交后映射 | 评论；#364 / #363 跟踪 |
| #188 #263 #264 | Scheduling lease/retry/settlement | schedule 阶段失败仍 settle execution、`ScheduleSettled` 无读者 | 评论；#345 / 计划 Task 7 跟踪 |
| #20 | prepared execution scope + context restore | 后台调度仍用 `JobExecutionScopes.Plain`，不恢复持久化 MessageContext | 评论；#366 跟踪 |
| #39 | logical reference 独立于 ownership | 校验已独立；取值分类/存在性目标仍分裂 | 评论；#369 跟踪 |
| #145 | 常量 holder 统一 | RFC 8288 relation 字面量散落 8 文件 | 评论；#365 跟踪 |
| #66 | 未使用依赖清理 | 被 #343 取代；`Resource.Foundation` 仍引用未使用的 `Security.Foundation` | 评论（在 #343/#372 跟踪） |
| #2 #3 | 旧包布局的 Security 依赖方向 | 现为反向耦合 `Transport.Grpc → Security.Foundation` | 评论；#367 跟踪 |
| #199 | Event RFC 8288 links | 无 Event HTTP 管理面，仍不适用 | 评论；#365 明确排除 |
| #146 #236 #333 #220 #222 #273 #275 | 文档与实现/优先级对齐 | 文档在后续提交后再次漂移（priority 表、cookbook 包、失效路径） | 评论；#370/#371 跟踪 |

另：24 个 closed issue 与 12 个新建 issue（#361–#372）均已留下交叉引用评论，避免"新 issue 不知道历史"或"closed 后重复讨论"。

### 11.3 结论/教训

1. **关闭声称 ≠ HEAD 事实**：本轮重开的 2 个 issue 都属于"关闭时验收成立、后续重构使其回退"的类型；对跨组件重构，关闭前应重新执行 acceptance 而非只验证当时提交。
2. **文档/优先级表是反复漂移点**：闭过的 #146/#220/#222/#236/#273/#275/#333 在本轮再次出现同类问题，因此计划把"唯一表格 + 从 HEAD 生成"作为文档 Task 13 的强制步骤，而不是逐行打补丁。
3. **收尾约定**：closed issue 的残余优先用 open successor + 交叉评论跟踪；只有当 acceptance 本身仍无 owner 时才重开，避免同一缺陷多 issue 并行。

## 12. Owner 追加反馈（2026-10-20 第二轮）与处置

owner 在复核后追加了 6 个方向性要求；本节记录核实结果、决策与落地状态。

| ID | 要求 | 核实结果 | 处置 |
|---|---|---|---|
| X-29 | `IFilterRequest` 疑为无用接口（似乎想统一含 Filter 的请求处理） | 确认无实现：`ListRequest` 有 `Filter/Language` 但未实现该接口，唯一消费者 `QueryableExtensions.ApplyFilter` 仅被自身测试使用；真实 filter owner 是 List/Purge 管线（含语言、residual/pushdown、错误映射） | **已移除**（`fefd7bae`，#374）：删除接口、helper 与测试；不引入第二套 filter 路径 |
| X-30 | 移除 `ValidateExistence` 及相关逻辑；框架不对资源 ref 做写入时检查，用户自行在业务逻辑/advisor 处理 | `AdviceValidateResourceReferences`、`AdviceValidateResourceReferenceExistence`、`ValidateExistence` 及注册/测试/文档全部存在（#359/#369 正在围绕它们设计） | **已移除**（`fefd7bae`，#373）：两个 advisor、attribute 选项、注册、测试、Tenancy 排序锚点与文档全部清理；`[ResourceReference]` 保留为声明式 canonical-name 关联标记；#369 关闭，#359/#39 更新说明 |
| X-31 | 分析 `AppDomainTypeCache` 是否仍有价值；有则框架内反射尽量使用，无则移除 | **有价值**：20+ 处热路径（Repository 行映射、Mapping mask、gRPC proto、JSON polymorphic、feature 依赖、ResourceNameDescriptor）；静态类型元数据缓存不违反每请求状态约束 | **已实现**（#378，`6fa8b7df`）：`GetAllTypes()` 增量重扫、`GetFields`/`GetProperty` 访问器、late-loaded polymorphic 可见性、D10 文档化；RequestObjectReader/EF customizer/PublicModel/ErrorClassifier/RepositoryBase/link contributor 的裸 `System.Type` 反射已收口；新增 `AppDomainTypeCacheShould` 测试。`GetMethod`/`MakeGenericMethod` 按 D10 保持直接调用 |
| X-32 | `Schemata.Core/State` 以侵入、面向结果方式实现 Tenancy 面向客户端(UA)的隔离，违背组件不耦合 | 确认：`Core/State` 的 10 个公开类型唯一消费者是 `Tenancy.Foundation/State`（11 文件：state profile、session/antiforgery/cookie/bearer），Core feature 还替换原生 `IAuthenticationService`、插入 state pipeline 分支 | **已实现**（#376，`c4ba6f18`）：客户端/UA 认证 scope 路由（`AuthenticationSchemeScope`/`IAuthenticationScopeProvider`/`ScopedAuthenticationService`/`ScopedAuthenticationHandlerProvider`）移入 `Schemata.Tenancy.Foundation/State`，Tenancy feature 自行 Replace `IAuthenticationService`；Core 回归原生注册。Core 仅保留通用的 per-request state-pipeline 分支能力（`IStatePipelineProvider`/`IStatePipelineValidator`/`StatePipelineExtensions`），作为 component 覆盖原生 session/antiforgery 中间件的唯一通用 seam；Core 注释中的 tenant 词汇已清零，新增 `AuthenticationFeatureShould` 断言只有原生 `IAuthenticationService` 描述符 |
| X-33 | `Flow.Repository`、`Scheduling.Repository` 考虑并入各自 Foundation（repository/cache 是基础设施，允许直接依赖） | 两包各仅 2 个文件，只依赖 `Entity.Repository` + 自身 Skeleton；Foundation 已引用 `Entity.Repository`，独立包不提供隔离 | **已实现**（#375，`06d7f3a9`）：两包文件/DI 扩展移入 Foundation/Repository，删除两 csproj 与 slnx 条目，更新 10 个 csproj、12 个测试源码、调度文档与 AGENTS 包数（90→88） |
| X-34 | 正式代码禁止 `foo!.Bar` 强制访问 nullable；按示例用模式匹配 + early return；多用 switch expression | HEAD `fefd7bae` 统计：强制访问 69 处、参数/语句位置 `!` 477 处、`null!`/`default!` 103 处（共 210 文件）；无编译器诊断可禁用该运算符 | **已实现**（#379，`4ed3f1d2`）：`src/**` 强制访问 `!` 清零（全局扫描 0）；移除 33 个 `null!`/`default!`（`required`/Try+NotNullWhen/局部函数），剩余 70 处按 EF 物化、wire DTO、BPMN parser、definite-assignment 契约在 AGENTS.md 集中豁免；普通参数位置 `arg!` 不在本规则范围。防回归采用评审规则（D9，写入 AGENTS.md），不新增 analyzer 包 |

### 12.1 决策点（追加）

| 决策 | 选项 | 建议 |
|---|---|---|
| D6 资源关联校验 | 保留（类型/存在）vs 移除 | owner 已定：**移除**；`[ResourceReference]` 仅声明，用户自担校验（已落地） |
| D7 包合并 | 保留 `Flow.Repository`/`Scheduling.Repository` vs 并入 Foundation | **并入**：repository/cache 属基础设施，component 可直接依赖；bridge 只保留跨 component 能力（Actor/Event/Scheduling） |
| D8 Core state 隔离 | 在 Core 保留通用分支能力 vs 全部移入 Tenancy | **全部移入 Tenancy**；只有当第二个非 tenancy 消费者出现时才在 Core 抽象通用 state 分支 |
| D9 null-forgiving 防回归 | Roslyn analyzer（新包）vs 评审规则 | 清理完成后优先在既有 generator/analyzer 资产内加规则或使用 repo 级检查；若必须新包，按“职责边界证明”新建 `analyzers/Schemata.Analyzers` |
| D10 AppDomainTypeCache 方法反射 | 增加 method cache vs 仅文档说明 | 先收口属性/字段反射；method cache 仅在实测热路径证明有收益时引入 |

### 12.3 第二轮落地提交（2026-10-21）

| Commit | 内容 |
|---|---|
| `06d7f3a9` | #375 包合并：`Schemata.Flow.Repository`/`Schemata.Scheduling.Repository` 并入 Foundation（90→88）；更新 slnx/ProjectReference/命名空间/调度文档/AGENTS |
| `c4ba6f18` | #376 状态隔离归位：认证 scope 路由四类型移入 Tenancy；Tenancy feature 安装 facade；Core 回归原生 `IAuthenticationService`；两个单测套件迁到 Tenancy.Tests |
| `6fa8b7df` | #378 AppDomainTypeCache 加固与反射收口；late-load/GetFields/GetProperty 测试；EF Core 显式引用 Common |
| `4ed3f1d2` | #379 null-forgiving 强制访问清零、null!/default! 清理与 AGENTS 规则；附带 `ExpressionCache` 泛型约束修正 |
| `96feb62a` | CI 修复：Actor 终止通知在 provider 已释放边界跳过 scope；TenantActor 集成测试改用 file-backed SQLite 并显式 drain，消除 net10.0 flake |
| `757e8723` | 文档/契约注释：70 处保留 `null!`/`default!` 逐处注明运行时契约；刷新 AGENTS 运行时路径并清除 Core state-pipeline seam 的 tenancy 词汇 |
| `33ee3616` | 测试稳定性：Scheduling lost-lease 用例租约 100ms→1s，避免慢 runner 上确认取消竞态 |
| `89fc7104` | #376 收尾：Core 注释中的 tenant 词汇清零；新增 `AuthenticationFeatureShould` 断言 Core 只注册原生 `IAuthenticationService`；plan/spec/ledger 验收文字与实现对齐 |
| `757e8723` | 收尾：为剩余 70 处 `null!`/`default!` 声明补充行内契约说明；修正 AGENTS 过期路径（Runtime dispatcher、passed AdviceContext）；Core state seam 去除 state-profile 措辞 |

验证：Release build 0 warning/0 error；本地受影响套件 net8.0 全绿（Core 106、Tenancy 106、Tenancy.Integration 157、Flow 235 + Integration 172、Scheduling 165 + Integration 31、Entity.Repository 57、Entity EF 98、Entity LinqToDB 87、Insight 105 + Http 57 + Grpc 19、Mapping 5 + AutoMapper 19 + Mapster 27、Authorization 1113 + Integration 292、Push 66 + Http 9 + Grpc 55、Report 139 + Integration 92、Resource 316 + Http 98 + Grpc 45、Security 272、Event.Foundation 32、Actor.Foundation 70、Messaging.Skeleton 33、Expressions.Cel 1124、Expressions.Aip 148、Common 65）；CI 结果见 GitHub 运行。

### 12.2 落地提交

- `fefd7bae refactor(Resource)!: remove write-time reference validation and dead filter interface`（X-29/X-30）：
  - 删除 `AdviceValidateResourceReferences`、`AdviceValidateResourceReferenceExistence`、`ValidateExistence`、注册与测试/文档；
  - 删除 `IFilterRequest`、`QueryableExtensions.ApplyFilter` 及测试；
  - `AdviceUpdateTenant` 排序锚点改为 `AdviceUpdateValidation.DefaultOrder + 10_000_000`；
  - 验证：Release build 0 warning/0 error；Entity.Repository 57/57、EF Core integration 98/98、Resource 316/316、Tenancy 100/100 + Integration 157/157（net8.0/net10.0）。
- `70d7aafb test(Flow): drop the removed typed-reference validation expectation`：删除 Flow 租户仓储中依赖已移除校验的 `Accept_Host_Defined_Subtypes_For_Typed_Resource_References` 用例；Flow.Integration.Tests 172/172（双 TFM）。
- 最终 `70d7aafb` 的 GitHub CI：**build（ubuntu/macos/windows + publish）、docs、analysis 三个 workflow 全部 success**（详见 §10 的 CI 记录）。
