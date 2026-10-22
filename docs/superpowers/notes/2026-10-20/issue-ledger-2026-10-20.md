# Gitea issue ledger — 2026-10-20 review round

Repository: `cyp/Schemata` (http://raid.tekoki.internal:9000/cyp/Schemata). Operations executed through the tekoki Gitea MCP server; each close/update carries a HEAD-evidence comment.

## Closed (25)

Verified fixed at HEAD `c44090aa` with file:line evidence (static review; no runtime execution):

- Authorization/Identity/SCIM: #352 #353 #354 #357 #358 #359
- Entity/Resource/Tenancy/Transport: #334 #336 #337 #338 #342 #347 #350 #356 #360 #197 #200 #202 #203 #204 #205
- Execution/Messaging/Flow: #285 #291 #355
- Hygiene: #296

Notes:
- #359 was fixed by `35d57331`, but that fix over-required `IResourceTypeResolver` for typed references and broke repository-only hosts; `c44090aa` corrected the policy (typed references validate against the declared target's `[CanonicalName]` pattern; polymorphic references require the resolver). Closed on the corrected state.
- #360 was fixed for Create/Update; the Delete/Undelete residual is tracked by new issue #363.
- The six link issues (#197 #200 #202 #203 #204 #205) were blocked by #196; the shared model/serializer dependency is satisfied and the remaining #196 scope (gRPC parity, relation constants) is tracked by #365.

## Commented, kept open (12)

- #196 — shared model/serializer/HTTP projection done; gRPC parity + relation constants remain (new #365).
- #198 — HTTP job/execution links done; gRPC metadata absent; owner/affected-resource bullets not applicable at HEAD.
- #201 — process/token/transition links done; definitions link is unfiltered and cannot identify a definition.
- #343 — items 1–4 done; mapper dead overloads remain (new #372).
- #345 — transaction split fixed; schedule-loss residual remains (plan Task 7).
- #349 — repository/scheduling/probe convergence done; automatic cache isolation missing (new #364, spec decision D1).
- #351 — request leg fixed; interactive success delivery still re-infers response mode (plan Task 2).
- #107 — flows registered; implicit ID token P0, discovery and end-to-end tests missing (new #361 + plan Task 3).
- #123/#124/#125 — SCIM CRUD exists; PATCH/filter/externalId/administrative-active missing (new #362 + plan Task 10/11).
- #61 — still unimplemented; capability roadmap Phase 4.

## Created (12)

| # | Title | Priority |
|---|---|---|
| #361 | Authorization: issue ID tokens for pure implicit responses | Critical |
| #362 | Identity: hold administrative Active on Identity admission and enforce it on token issuance | High |
| #363 | Resource: own the commit for Delete and Undelete before mapping the response | High |
| #364 | Tenancy: frame framework-internal cache keys and settle the host cache fallback | High |
| #365 | Transport: project one RFC 8288 link contributor seam over HTTP and gRPC | High |
| #366 | Scheduling: restore the persisted message context for background dispatch | High |
| #367 | Component boundaries: remove the transport→security and Foundation→transport/scheduling couplings | Medium |
| #368 | Flow: unify the wire requests and dispatch commands | Medium |
| #369 | Entity: unify `[ResourceReference]` value classification and existence target | Medium |
| #370 | Docs: remove blanket compliance claims and document the enabled OAuth/OIDC flows | High |
| #371 | Docs: align feature priority tables, package inventory and stale references with HEAD | Medium |
| #372 | Framework hygiene round 2: dead surfaces, mapper overloads, naming collisions and policy hacks | Low |

## Not actioned

- #100 #101 #102 #108 #109 #114 #115 #118 #126 #127 #128 #110 #112 — no code/evidence for their acceptance at HEAD; remain in the capability roadmap (spec §7) without new comments (their bodies are still accurate).
- #27 #36 #38 #41 #44 #49 #53 #59 — feature backlog, unchanged.


## Closed issue 交叉分析（补充轮）

本轮开始时仓库有 296 个 closed issue；通过 Gitea MCP 全量拉取（7 页/321 条，含本轮关闭的 25 条）并与 HEAD `a73624f7` 交叉核对：

- **重新打开（2）**：#24（Tenancy cache/query 按执行身份隔离的验收未达成：租户回落到未加 frame 的 host cache、幂等 key 无 tenant；实施跟踪 #364）、#135（成功路径仍绕过已验证 callback 上下文；实施跟踪 #351/计划 Task 2）。
- **closed issue 证据评论（24，含 2 个重开）**：#2、#3、#20、#24、#39、#66、#97、#135、#139、#145、#146、#188、#192、#195、#199、#220、#222、#236、#263、#264、#273、#275、#318、#333（各带 HEAD file:line 证据与 open successor 指针）。
- **新建 issue 反向引用（12）**：#361–#372 均补充 closed 前身评论，避免重复讨论。
- 结论与教训见 `docs/superpowers/specs/2026-10-20-post-review-remediation-spec.md` §11（关闭声称 ≠ HEAD 事实；文档/优先级表反复漂移；残余优先用 open successor + 交叉评论跟踪）。

## Owner 追加反馈轮（2026-10-20 第二轮）

- **已落地并关闭**：
  - #373 `Entity: remove write-time [ResourceReference] validation` — 两个 advisor、`ValidateExistence`、注册、测试、文档全部移除；`[ResourceReference]` 保留声明式标记；commit `fefd7bae`。
  - #374 `Resource: remove dead IFilterRequest and ApplyFilter helper` — 无实现接口与仅测试使用的 helper 删除；commit `fefd7bae`。
  - #369 因 owner 决策关闭（不再需要统一值分类），#359/#39 已评论说明新方向。
- **新建并完成（2026-10-21 全部关闭）**：#375（合并 Flow.Repository/Scheduling.Repository 包，`06d7f3a9`）、#376（Tenancy 客户端(UA)状态隔离移出 Schemata.Core，`c4ba6f18`；#377 重复已关闭）、#378（AppDomainTypeCache 保留+加固+反射收口，`6fa8b7df`）、#379（移除 null-forgiving 强制访问并按模式匹配/switch expression 现代化，`4ed3f1d2`）。
- 对应 spec §12（X-29…X-34、决策 D6–D10）与 plan Task 8（已完成）、Task 15–18。

## Owner 追加反馈轮（2026-10-20 第二轮）— 完成状态（2026-10-21）

| Issue | 状态 | 实现 |
|---|---|---|
| #373 write-time ref validation removal | closed | `fefd7bae` |
| #374 dead IFilterRequest/ApplyFilter | closed | `fefd7bae` |
| #375 fold Flow/Scheduling Repository packages | **closed** | `06d7f3a9`（90→88 packages；slnx/ProjectReference/测试/调度文档/AGENTS 同步） |
| #376 move tenancy client state-profile auth routing out of Core | **closed** | `c4ba6f18`（认证 scope 路由移入 Tenancy；Core 保留通用 state-pipeline seam 并回归原生认证注册） |
| #377 duplicate | closed | — |
| #378 AppDomainTypeCache keep/harden + reflection routing | **closed** | `6fa8b7df`（增量重扫/GetAllTypes/GetFields/GetProperty；`AppDomainTypeCacheShould` 测试） |
| #379 null-forgiving forced access / nullable masking | **closed** | `4ed3f1d2`（src forced access = 0；null!/default! 清理后剩余契约例外写入 AGENTS.md；D9 评审规则） |

补充收尾：`96feb62a`（Actor 终止通知 provider 释放边界 + TenantActor 集成测试稳定化）、`757e8723`（为全部剩余 null!/default! 声明补充运行期契约说明、修正 AGENTS 过期路径、Core state seam 去 tenancy 措辞）。GitHub/Gitea `master` 均为 `757e8723`。
