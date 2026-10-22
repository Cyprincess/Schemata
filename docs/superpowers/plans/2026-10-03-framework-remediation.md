# Schemata 框架修复与新能力实施计划（2026-10-03）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按已定稿规格修复 35 项当前缺陷并落地 U10/U11 新能力合同，全部完成后达到规格 §5.3 的六项完成条件。

**Architecture:** 缺陷按组件聚类为 10 个集成任务，每任务内逐 issue 修复并独立提交；新能力 U10（关联消息/timer/lease/Web Push）与 U11（RFC 9421 签名、分页 continuation、AIP-155 幂等）逐 issue 实施。所有改动在既有 owner 内完成，不新建机制或包；可选能力一律经 DI/advisor 安装。

**Tech Stack:** .NET SDK 10.0.201（global.json）、net8.0/net10.0、Arcade SDK、xUnit v2 + Moq、coverlet、Conventional Commits（cocogitto）。

**Spec:** `docs/superpowers/specs/2026-10-03-framework-remediation.md` — 本计划不重复规格的合同与验收文本，逐任务引用其章节；执行者必须同时打开规格对应单元与 issue 正文。

## Global Constraints

逐条来自规格 §1、§4.1 U9 与 §5，所有任务隐含遵守：

- 一个技术职责只有一个 owner；transport、facade、后台桥接只适配或调用该 owner，禁止同功能第二路径或 manual new 维持平行实现。
- 可选能力由实际服务/advisor 的 DI 安装表达；运行时通过 `GetService`/`GetServices`/`GetKeyedService(s)` 或注入 `IEnumerable<T>` 探测；禁止功能状态位、bool/枚举分支、descriptor 清理。
- Advisor 一律 `TryAddEnumerable` 注册；handler 注册在 `IRequestHandler<,>` 后（可 keyed）；禁止在 advisor/AdviceContext/wrap 中传业务数据或状态标志。
- 无泛型分裂：同一功能只保留一条 generic 路径；常量与静态字典归 `SchemataConstants` 或对应组件的 `Constants` 类型。
- 错误遵循 AIP-193：异常派生 `SchemataException`，工厂在 `SchemataResourceErrors`，reason 常量归 `SchemataConstants.ErrorReasons`。
- 活跃开发期：无向后兼容承诺、无内置数据迁移；API cutover 全部仓内调用方一次改净，不留 alias/shim/双写。
- 不新建测试项目，新场景加入最近现有 suite；不为测试而测试，永久测试只钉消费者可观察行为、边界、事务、顺序、失败语义。
- 测试节奏：每个任务实现完成后 build 一次，随后 `dotnet test <suite> -c Release --no-build` 最终运行一次并修到绿；禁止同一命令重复执行，禁止任务间强制 rebuild。
- 提交：Conventional Commits，`cog check` 通过，多 `-m` 组织，subject ≤100 字符；`git add` 只加任务显式文件。禁止提交 `.superpowers/`、`docs/superpowers/`、`benchmarks/`、`docs/audits/`。
- Gitea issue 状态更新与评论（cyp/Schemata）在获得授权后由集成 owner 统一执行，引用实际行为证据；push/merge 另行授权。

## Review Focus

规格未逐条钉死、但最可能咬到使用者的输入类与失败模式，按可能性排序；每条已把测试钉到所属任务的步骤中：

1. **Authorization 协议面**（任务 1）：超范围 JWT NumericDate 必须协议化报错而非 CLR 异常或静默接受；交互授权全程保留已验证 RAR 集；最终批准必须强制 requested subject 一致；claim value qualifier 必须先于 destination 应用；proof/assertion validator 生命周期必须与 keyed scoped token store 对齐。
2. **Flow 引擎边界**（任务 6）：loop 每次迭代都调用 activity body；CallActivity 经每条自动路由路径执行；durable compensation 绑定保留真实 boundary identity；signal 订阅以 arm 时同一 identity 移除；boundary 投递之间刷新 working token 快照；注册期限制 typed payload 可达性遍历深度。
3. **Scheduling claim 后失败与身份**（任务 4）：claim 后 pre-body 管道失败必须 settle；durable dispatch 全程携带发起 principal；取消只 commit 一行 pending。
4. **持久化与传输边界**（任务 7）：EF 嵌套 JSON 快照按内容比较（值相等不产生假变更）；gRPC 完整保留已声明的 quota 违规 payload；RabbitMQ reply tracking 在全发送边界（含异常与超时）释放。
5. **横向与生成合同**（任务 10、11–18）：字符串常量归并到组件 Constants 后无重复持有者；Modeling 生成实体满足基类声明的外部接口合同；marker/descriptor 断言被消费者可见协议结果取代后回归仍然有效；U10/U11 改动不分裂 capability 与 wire 行为。

## Phase 1: 缺陷修复（规格 §2.1，按 §6 聚类）

### Task 1: Authorization 协议与文档缺陷（#153、#154、#155、#161、#162、#180、#146）

> **已完成（2026-10-04）**：commits 83f9a4f2..27eaa9e7（9 个，含 1 个 fix round），任务评审 Approved；详见台账 `.superpowers/sdd/framework-remediation-2026-10-03/progress.md`。

**Files:**
- Modify: `src/Schemata.Authorization.Foundation/`（validator 生命周期、consent admission、interaction approval、JWT claims、RAR、claim qualifiers；recon 定位精确文件）
- Modify: `src/Schemata.Authorization.Skeleton/`（若协议类型需扩展）
- Modify: `docs/`（#146 recipe 文档，独立资源名与当前 API）
- Test: `tests/Schemata.Authorization.Tests/`、`tests/Schemata.Authorization.Integration.Tests/`

**Interfaces:**
- Consumes: 规格 §3 U2/U3 合同文本；各 issue 正文验收。
- Produces: 无新公共接口预期；若需扩展协议类型，签名以 issue 正文为准并写入本任务完成记录。

- [ ] **Step 1: 逐 issue recon** — 打开 #153/#154/#155/#161/#162/#180/#146 正文与规格 §3 U2/U3，列出每项的 owner 文件、当前行为与期望行为矩阵；写入任务笔记（不进仓库）。
- [ ] **Step 2: #153 修复** — proof/assertion validator 的生命周期与 keyed scoped token store 对齐（规格 §2.1：align proof and assertion validator lifetimes with keyed scoped token stores）；提交。
- [ ] **Step 3: #154 修复** — reusable consent admission 纳入 descriptor-owned authorization details；提交。
- [ ] **Step 4: #155 修复** — 最终 interaction approval 强制 requested subject；提交。
- [ ] **Step 5: #161 修复** — 超范围 JWT NumericDate claims 协议化处理；提交。
- [ ] **Step 6: #162 修复** — 交互授权全程保留已验证 RAR 集；提交。
- [ ] **Step 7: #180 修复** — 个别 claim value qualifier 先于 destination 应用；提交。
- [ ] **Step 8: Review Focus 1 测试** — 在 Authorization.Tests 增加协议面用例：超范围 NumericDate 的协议错误、交互中 RAR 集保留、requested subject 不一致拒绝、qualifier→destination 顺序、validator 生命周期与 keyed store 对齐（逐项以 issue 正文验收为准）。
- [ ] **Step 9: #146 文档** — 更新 Authorization recipe 文档为独立资源名与当前 API；独立提交 `docs: …（#146）`。
- [ ] **Step 10: 构建** — `dotnet build Schemata.slnx -c Release`，显式确认 0 错误（不得只看尾部输出）。
- [ ] **Step 11: 测试（唯一一次）** — `dotnet test tests/Schemata.Authorization.Tests -c Release --no-build` 与 `dotnet test tests/Schemata.Authorization.Integration.Tests -c Release --no-build`；修到全绿。记录各 suite 通过数作为交付证据。

### Task 2: Resource/Insight 映射、作用域与阶段控制（#156、#157、#179、#181）

> **已完成（2026-10-04）**：commits 27eaa9e7..4871e47c（6 个，含 1 个 fix round），任务评审 Needs fixes→复审通过；#181 文档 cutover 与 Report.Tests 迁移回归均已闭环。

**Files:**
- Modify: `src/Schemata.Resource.Foundation/`、`src/Schemata.Resource.Skeleton/`（full-update 映射、custom-method scope、可选 stage 控制）
- Modify: `src/Schemata.Resource.Grpc/`（#157 scope 经 gRPC 保留；recon 确认）
- Modify: `src/Schemata.Insight.Foundation/`（#179 source alias 降低）
- Test: `tests/Schemata.Resource.Tests/`、`tests/Schemata.Resource.Http.Integration.Tests/`、`tests/Schemata.Resource.Grpc.Integration.Tests/`、`tests/Schemata.Insight.Tests/`

**Interfaces:**
- Consumes: 规格 §3 U5/R05 合同；各 issue 正文验收。
- Produces: 可选 stage 控制的 DI 注册形态（任务完成时记录，供任务 10 横向检查引用）。

- [ ] **Step 1: recon** — 定位 full-update 映射路径、custom-method scope 在 gRPC 与 owner dispatch 的传递点、Insight filter/order 编译的 alias 处理、可选 stage 控制的现状（状态位或 DI）。
- [ ] **Step 2: #156 修复** — full update 映射保留已加载的 system fields；提交。
- [ ] **Step 3: #157 修复** — registered custom-method scope 经 gRPC 与 owner dispatch 全程保留；提交。
- [ ] **Step 4: #179 修复** — Insight 在 repository filter 与 order 编译前降低 source alias；提交。
- [ ] **Step 5: #181 修复** — 可选 stage 控制整合到 DI 注册（R05：安装表达而非状态位）；提交。
- [ ] **Step 6: 测试（唯一一次）** — build 后 `--no-build` 运行四个 suite：Resource.Tests、Resource.Http.Integration、Resource.Grpc.Integration、Insight.Tests；修到全绿。

### Task 3: Identity 删除门控（#158）

> **已完成（2026-10-04）**：commit 5c6a0d9c，任务评审 Approved；实际 owner 为 Identity.Skeleton/Mutations（brief 所写 Foundation 仅为 DI 装配，偏差已记录）。

**Files:**
- Modify: `src/Schemata.Identity.Foundation/`（user/role 删除与依赖 cleanup；recon 定位）
- Test: `tests/Schemata.Identity.Tests/`、`tests/Schemata.Identity.Integration.Tests/`

**Interfaces:**
- Consumes: 规格 §3 U1 合同。
- Produces: 无新接口预期；依赖 cleanup 只在主删除实际应用后执行。

- [ ] **Step 1: recon** — 定位 user/role 删除路径与依赖 cleanup 的触发点，确认 cleanup 是否在主删除未应用时也会执行。
- [ ] **Step 2: 修复** — 依赖 cleanup 门控在主 user/role 删除实际应用之后；提交 `fix(identity): gate dependent cleanup on an applied primary user or role deletion (#158)`。
- [ ] **Step 3: 测试（唯一一次）** — build 后 `--no-build` 运行 Identity.Tests 与 Identity.Integration.Tests；断言主删除失败时 cleanup 不执行、主删除应用后 cleanup 执行；修到全绿。

### Task 4: Scheduling 执行 owner 与配置（#163、#167、#168、#169）

> **已完成（2026-10-04）**：commits 5c6a0d9c..346271d7（4 个），任务评审 Approved；principal 链与 settle 语义签名已记录在 task-4-report.md，供 Task 13 对齐。#167 的 producer 迁移触及 Resource/Report/Push/Authorization 四处调用点（偏差已记录并验证）。

**Files:**
- Modify: `src/Schemata.Scheduling.Foundation/`（执行/claim/settle/dispatch owner）
- Modify: `src/Schemata.Scheduling.Event/`（#169 删除过时 execution-interception 配置）
- Test: `tests/Schemata.Scheduling.Tests/`、`tests/Schemata.Scheduling.Integration.Tests/`

**Interfaces:**
- Consumes: 规格 §2.1 行（#163/#167/#168 与 U10 #188 相邻）、§3 R05（#169）。
- Produces: durable dispatch 的 principal 传递与 settle 语义的最终签名（任务完成时记录，供任务 11 #188 对齐）。

- [ ] **Step 1: recon** — 定位 claim/settle/dispatch 路径与 Scheduling.Event 的过时配置注册点。
- [ ] **Step 2: #163 修复** — 取消 commit 为单一 pending 行，不再产生 cancelled 行 + stale retry；提交。
- [ ] **Step 3: #167 修复** — durable trigger dispatch 全程携带发起 principal；提交。
- [ ] **Step 4: #168 修复** — claim 后 pre-body 管道失败必须 settle（含异常路径）；提交。
- [ ] **Step 5: Review Focus 3 测试** — Scheduling.Tests 增加：claim 后 pre-body 抛错 → execution 已 settle；durable dispatch 的 principal 断言；取消只产生一行 pending。
- [ ] **Step 6: #169 修复** — 删除过时 execution-interception 配置及全部引用；提交。
- [ ] **Step 7: 测试（唯一一次）** — build 后 `--no-build` 运行 Scheduling.Tests 与 Scheduling.Integration.Tests；修到全绿。

### Task 5: Push 订阅保留（#166）

**Files:**
- Modify: `src/Schemata.Push.Foundation/`（订阅存储；recon 确认）
- Test: `tests/Schemata.Push.Tests/`

**Interfaces:**
- Consumes: 规格 §3 R13 合同。
- Produces: 无新接口预期。

- [ ] **Step 1: recon** — 定位地址重建时订阅行的删除/重建路径。
- [ ] **Step 2: 修复** — 地址重建保留既有订阅行；提交 `fix(push): restore the retained subscription row when an address is recreated (#166)`。
- [ ] **Step 3: 测试（唯一一次）** — build 后 `--no-build` 运行 Push.Tests；修到全绿。

### Task 6: Flow 引擎与 BPMN 缺陷（#170、#171、#172、#173、#174、#175、#176、#177、#178）

**Files:**
- Modify: `src/Schemata.Flow.Foundation/`（timer catch 装配、signal broadcast 候选、signal 订阅 identity、payload 可达性遍历、token 快照刷新）
- Modify: `src/Schemata.Flow.Bpmn/`（loop activity body、child snapshot 路由、CallActivity、compensation 绑定）
- Test: `tests/Schemata.Flow.Tests/`、`tests/Schemata.Flow.Integration.Tests/`、`tests/Schemata.Flow.Bpmn.Tests/`、`tests/Schemata.Flow.Bpmn.Conformance.Tests/`（Windows，按规格 `--filter "Speed!=Fast"` 仅在相关改动触发时）

**Interfaces:**
- Consumes: 规格 §3 R09 合同；#170/#171/#174 分别与 U10 #187/#186/#186 关联，保持兼容任务 11/12。
- Produces: timer catch 装配与 signal 订阅 identity 的最终语义（任务完成时记录，供任务 11/12 对齐）。

- [ ] **Step 1: recon** — 逐项定位：timer catch 装配点、signal broadcast 候选计算、loop 迭代 body 调用、child snapshot 路由、signal 订阅登记/移除、CallActivity 路由路径、compensation 绑定身份、payload 可达性遍历、token 快照刷新点。
- [ ] **Step 2: #170 修复** — token 在 event gateway 等待时 arm 外发 timer catch；提交。
- [ ] **Step 3: #171 修复** — signal broadcast 候选含 active boundary hosts；提交。
- [ ] **Step 4: #172 修复** — BPMN 每次 loop 迭代都调用 activity body；提交。
- [ ] **Step 5: #173 修复** — called child snapshot 经 transition owner 路由；提交。
- [ ] **Step 6: #174 修复** — signal 订阅以 arm 时的同一 identity 移除；提交。
- [ ] **Step 7: #175 修复** — CallActivity 经每条自动路由路径执行；提交。
- [ ] **Step 8: #176 修复** — durable compensation 绑定保留真实 boundary identity；提交。
- [ ] **Step 9: #177 修复** — 注册期限制 typed payload 可达性遍历；提交。
- [ ] **Step 10: #178 修复** — boundary 投递之间刷新 working token 快照；提交。
- [ ] **Step 11: Review Focus 2 测试** — Flow.Tests/Integration 增加：loop 多迭代 body 均执行、CallActivity 各自动路由、compensation 身份断言、同 identity 移除订阅、快照刷新可观察、遍历深度上界（逐项以 issue 正文验收为准）。
- [ ] **Step 12: 测试（唯一一次）** — build 后 `--no-build` 运行 Flow.Tests、Flow.Integration.Tests、Flow.Bpmn.Tests；BPMN 改动涉及时加跑 Conformance（`--filter "Speed!=Fast"`）；修到全绿。

### Task 7: Entity/Transport/Messaging 边界（#159、#160、#165）

**Files:**
- Modify: `src/Schemata.Entity.EntityFrameworkCore/`（#159 嵌套 JSON 快照比较）
- Modify: `src/Schemata.Transport.Grpc/`（#160 quota 违规 payload）
- Modify: `src/Schemata.Messaging.RabbitMq/`（#165 reply tracking 释放）
- Test: `tests/Schemata.Entity.EntityFrameworkCore.Integration.Tests/`（或 recon 确认的最近 suite）、`tests/Schemata.Transport.Grpc.Tests/`、`tests/Schemata.Messaging.RabbitMq.Tests/`

**Interfaces:**
- Consumes: 规格 §3 R17/R14/R06 合同。
- Produces: 无新接口预期。

- [ ] **Step 1: #159 修复** — EF Core 嵌套 JSON 快照按内容而非引用比较（值相等不产生假变更）；提交。
- [ ] **Step 2: #160 修复** — gRPC 完整保留已声明的 quota 违规 payload；提交。
- [ ] **Step 3: #165 修复** — reply tracking 在全发送边界（含异常/超时）释放；提交。
- [ ] **Step 4: Review Focus 4 测试** — EF 集成测试断言内容相等快照不误判变更、内容变化正确检测；Grpc.Tests 断言 quota 违规 payload 完整；RabbitMq.Tests 断言异常/超时路径 reply tracking 释放。
- [ ] **Step 5: 测试（唯一一次）** — build 后 `--no-build` 运行三个 suite；修到全绿。

### Task 8: Report 物化前执行 Insight plan advisors（#182）

**Files:**
- Modify: `src/Schemata.Report.Foundation/`（物化管道与 Insight 集成点；recon 确认）
- Test: `tests/Schemata.Report.Tests/`、`tests/Schemata.Report.Integration.Tests/`

**Interfaces:**
- Consumes: 规格 §3 R08 合同。
- Produces: 无新接口预期。

- [ ] **Step 1: recon** — 定位 report 物化管道与 Insight plan advisors 的当前调用顺序。
- [ ] **Step 2: 修复** — Insight plan advisors 在 report 物化之前运行；提交 `fix(report): run Insight plan advisors before report materialization (#182)`。
- [ ] **Step 3: 测试（唯一一次）** — build 后 `--no-build` 运行 Report.Tests 与 Report.Integration.Tests；断言 advisor 效果反映在物化结果中；修到全绿。

### Task 9: Actor activation 处置所有权（#164）

**Files:**
- Modify: `src/Schemata.Actor.Foundation/`（activation 实例管理与释放）
- Test: `tests/Schemata.Actor.Foundation.Tests/`

**Interfaces:**
- Consumes: 规格 §3 U7 合同。
- Produces: 无新接口预期。

- [ ] **Step 1: recon** — 定位 supervision 替换 activation 实例时的释放路径。
- [ ] **Step 2: 修复** — 框架对被替换实例 own disposal；提交 `fix(actor): own disposal for activation instances replaced by supervision (#164)`。
- [ ] **Step 3: 测试（唯一一次）** — build 后 `--no-build` 运行 Actor.Foundation.Tests；修到全绿。

### Task 10: 横向常量、测试断言、文档与 Modeling（#145、#147、#183、#185）

**Files:**
- Modify: 相关 `src/` 常量持有者（#145：新增字符串常量归并到组件 `Constants`/`SchemataConstants`）
- Modify: 相关 `tests/`（#147：marker/descriptor 断言替换为消费者可见协议结果断言）
- Modify: `docs/`（#183：module lifecycle 示例对齐 reflection-dispatched 方法）
- Modify: `generators/Schemata.Modeling.Generator/`（#185：满足 entity 基类声明的外部接口合同）
- Test: `tests/Schemata.Modeling.Tests/` 及 #147 触及的 suite

**Interfaces:**
- Consumes: 规格 §2.1 行与各 issue 正文。
- Produces: 无。

- [ ] **Step 1: #145** — 找出新建字符串常量持有者（static 类），归并到对应组件 `Constants`/`SchemataConstants`；编译保证无重复定义；提交 `refactor(contracts): …（#145）`。
- [ ] **Step 2: #147** — 按 issue 正文清单把 marker/descriptor 断言替换为消费者可见协议结果断言；提交 `test: …（#147）`。
- [ ] **Step 3: #183** — module lifecycle 文档示例对齐 reflection-dispatched 方法；提交 `docs: …（#183）`。
- [ ] **Step 4: #185** — Modeling.Generator 生成满足 entity 基类声明的外部接口合同；提交 `fix(modeling): …（#185）`。
- [ ] **Step 5: Review Focus 5（前半）测试** — Modeling.Tests 断言生成实体满足外部接口合同（编译+行为）。
- [ ] **Step 6: 验证（唯一一次）** — build 后 `--no-build` 运行 Modeling.Tests 及 #147 触及 suite；`docfx build docs/docfx.json` 确认 #183 告警消除（若本机有 docfx）。

## Phase 2: U10 可靠执行基础能力（规格 §4 U10；#186–#190）

> 任务 11–15 各自独立可交付。#187 依赖任务 6（#170）的 timer 修复并与其合并验收；#188 依赖任务 4 的 Scheduling settle/principal 语义记录；#189 依赖任务 6 的持久化语义记录。

### Task 11: 事件业务关联与 Flow.Event 信号匹配（#186）

**Files:**
- Modify: `src/Schemata.Event.Foundation/`（事件关联元数据与订阅侧过滤）、`src/Schemata.Flow.Event/`（信号匹配复用同一关联机制；recon 确认 bridge 归属）
- Test: `tests/Schemata.Event.Foundation.Tests/`、`tests/Schemata.Event.Integration.Tests/`、`tests/Schemata.Flow.Tests/`、`tests/Schemata.Flow.Integration.Tests/`

**Interfaces:**
- Consumes: 规格 §4 U10 第一条（#186）。
- Produces: 关联元数据的载体与过滤 API（任务完成时记录，供后续关联消费方复用）。

- [ ] **Step 1: recon** — 定位事件发布/订阅匹配路径与 Flow.Event 信号匹配实现，确认二者当前是否各有一套关联逻辑。
- [ ] **Step 2: 实现** — 事件携带业务关联元数据，订阅侧可按关联过滤；Flow.Event 信号匹配改用同一关联机制，删除第二套关联存储/逻辑；提交 `feat(event): carry business correlation metadata with subscription-side filtering (#186)`。
- [ ] **Step 3: 测试（唯一一次）** — build 后 `--no-build` 运行 Event.Foundation.Tests、Event.Integration.Tests、Flow.Tests、Flow.Integration.Tests；断言关联过滤命中/不命中与 Flow 信号一次匹配；修到全绿。

### Task 12: Flow 定时器武装/取消/重建/身份区分（#187，与 #170 合并验收）

**Files:**
- Modify: `src/Schemata.Flow.Foundation/`（timer 服务 owner；任务 6 已在此修复 #170）
- Test: `tests/Schemata.Flow.Tests/`、`tests/Schemata.Flow.Integration.Tests/`

**Interfaces:**
- Consumes: 规格 §4 U10 第二条（#170 + #187 合并验收）；任务 6 产出的 timer 装配语义记录。
- Produces: timer 身份区分与取消/重建的失败语义（AIP-193 错误）。

- [ ] **Step 1: recon** — 基于任务 6 的 timer 修复定位网关分支武装、取消、重建与身份校验点。
- [ ] **Step 2: 实现** — 网关分支武装、timer 取消/重建、身份不符拒绝；提交 `feat(flow): arm, cancel, and re-create timers with identity separation (#187)`。
- [ ] **Step 3: 合并验收测试（唯一一次）** — build 后 `--no-build` 运行 Flow.Tests 与 Flow.Integration.Tests；断言覆盖 #170 与 #187 的联合场景：token 在 event gateway 等待时 timer 已 arm、取消后不再触发、重建后按新身份触发、身份不符拒绝；修到全绿。

### Task 13: Scheduling 执行存储 provider 合同与默认 repository provider（#188）

**Files:**
- Modify: `src/Schemata.Scheduling.Skeleton/`（执行存储 provider 合同接口）、`src/Schemata.Scheduling.Foundation/`（lease、重试、崩溃恢复语义落实到合同）
- Create: 默认 repository provider 包（按规格"默认 repository 实现以 provider 包提供"；命名沿用既有 provider 约定 `Schemata.Scheduling.<Store>`，recon 后定名；仅此一个经规格授权的新包，不新建其他机制）
- Test: `tests/Schemata.Scheduling.Tests/`、`tests/Schemata.Scheduling.Integration.Tests/`（默认 provider 测试沿用仓内 SQLite fixtures）

**Interfaces:**
- Consumes: 规格 §4 U10 第四条；任务 4 产出的 settle/principal 语义记录。
- Produces: 执行存储 provider 接口（lease/重试/崩溃恢复语义的完整签名）与默认实现的注册扩展（任务完成时记录，供应用替换后端时引用）。

- [ ] **Step 1: recon** — 定位当前 lease/重试/崩溃恢复实现与存储耦合点，确定 seam 边界。
- [ ] **Step 2: 定义合同** — 执行存储 provider 接口承载 lease、重试上限、崩溃恢复语义；执行实体用途限框架内部可靠调度与审计（XML 文档写明）。
- [ ] **Step 3: 默认实现** — 以 provider 包提供基于既有 repository 的默认实现，注册扩展遵循 Global Constraints 的 DI 表达；应用可替换后端。
- [ ] **Step 4: 提交** — `feat(scheduling): define the execution-store provider contract with a default repository provider (#188)`。
- [ ] **Step 5: 测试（唯一一次）** — build 后 `--no-build` 运行 Scheduling 两个 suite；断言 lease 过期回收、重试上限、崩溃恢复经默认 provider 可观察；修到全绿。

### Task 14: Flow 外部副作用意图/完成记录 provider seam（#189）

**Files:**
- Modify: `src/Schemata.Flow.Foundation/`（引擎消费的意图/完成记录 seam；稳定 request_id 生成与转发）
- Test: `tests/Schemata.Flow.Tests/`、`tests/Schemata.Flow.Integration.Tests/`

**Interfaces:**
- Consumes: 规格 §4 U10 第五条；任务 6 的持久化语义记录。
- Produces: 意图/完成记录 provider 接口（含稳定 request_id、外部结果查询与同标识重发两条恢复路径、"结果未知"上报语义；任务完成时记录签名）。

- [ ] **Step 1: 实现** — 意图携带稳定 request_id 转发外部系统；恢复路径：外部协议支持结果查询则查询，否则以同一标识重发由接收端去重；两者都不支持时引擎上报"结果未知"，不承诺 exactly-once；意图记录随 transition 回滚消失（与任务 4/#188 的语义一致）；提交 `feat(flow): record external-effect intents and completions behind an engine provider seam (#189)`。
- [ ] **Step 2: 测试（唯一一次）** — build 后 `--no-build` 运行 Flow.Tests 与 Flow.Integration.Tests；断言三条路径：结果查询成功、同标识重发去重、两者皆缺时报"结果未知"；修到全绿。

### Task 15: Web Push 内置投递 provider（#190）

**Files:**
- Modify: `src/Schemata.Push.Foundation/`（或 recon 确认 provider 安放点；首选 Web Push 实现）
- Test: `tests/Schemata.Push.Tests/`

**Interfaces:**
- Consumes: 规格 §4 U10 第六条；RFC 8030（投递）、RFC 8291（消息加密）、RFC 8292（VAPID）——实现前从 rfc-editor.org 读取三份原文的必须行为节。
- Produces: 框架内置 Web Push 投递 provider 的注册扩展（DI 表达）。

- [ ] **Step 1: 读标** — 读取 `https://www.rfc-editor.org/rfc/rfc8030.txt`、`rfc8291.txt`、`rfc8292.txt` 的必须行为清单。
- [ ] **Step 2: 实现** — Web Push 投递 + 消息加密 + VAPID，签名可验；Push 核心不增加 outbox 式投递账本或重试状态机，发送失败的重试由应用通过 Scheduling 表达（XML 文档写明该边界）；提交 `feat(push): add a built-in Web Push delivery provider (#190)`。
- [ ] **Step 3: 测试（唯一一次）** — build 后 `--no-build` 运行 Push.Tests；断言实际投递与签名可验；修到全绿。

---

## Phase 3: U11 出站传输与安全（规格 §4 U11；#192、#194、#195）

### Task 16: RFC 9421 HTTP 消息签名（#192）

**Files:**
- Create/Modify: `src/Schemata.Security.Foundation/`（签名核心，密钥归既有 Security 所有权）、HTTP 与 gRPC 绑定（同面定义共享；recon 确定安放位置，优先既有包，不新建包）
- Test: `tests/Schemata.Security.Tests/`、`tests/Schemata.Transport.Grpc.Tests/`

**Interfaces:**
- Consumes: 规格 §4 U11 第一条（RFC 9421 §2.1/§2.2/§3.3/§4.1/§4.2/§6.2；实现前从 rfc-editor.org 读取原文对应节）。
- Produces: 声明参与签名的 attribute/注册标记与 Signature-Input/Signature 输出 API（任务完成时记录，供任务 17 复用）。

- [ ] **Step 1: 读标** — 从 `https://www.rfc-editor.org/rfc/rfc9421.txt` 读取 §2.1、§2.2、§3.3、§4.1、§4.2、§6.2；提取必须行为清单。
- [ ] **Step 2: 实现** — 派生组件/字段标记、签名计算与输出、算法注册表初始集；HTTP 与 gRPC 同面定义；提交 `feat(security): add RFC 9421 HTTP message signatures shared by HTTP and gRPC (#192)`。
- [ ] **Step 3: Review Focus 5（后半）测试** — Security.Tests 与 Grpc.Tests 断言两 transport 签名定义同源可验。
- [ ] **Step 4: 测试（唯一一次）** — build 后 `--no-build` 运行两个 suite；修到全绿。

### Task 17: Resource 分页 continuation 调用作用域（#194）

**Files:**
- Modify: `src/Schemata.Resource.Foundation/`（continuation 签发/校验，复用 R15 protected continuation 编解码）
- Test: `tests/Schemata.Resource.Tests/`、HTTP/gRPC integration suites

**Interfaces:**
- Consumes: 规格 §4 U11 第二条、§3 R15 codec；任务 16 的签名能力（若用于保护 continuation）。
- Produces: continuation 绑定的 caller/租户/参数校验失败语义（AIP-193 错误）。

- [ ] **Step 1: 实现** — continuation 绑定 caller 身份与授权上下文，跨调用方/租户/参数不可复用；提交 `feat(resource): bind pagination continuations to the calling scope (#194)`。
- [ ] **Step 2: 测试（唯一一次）** — build 后 `--no-build` 运行 Resource.Tests 与 HTTP/gRPC integration；断言跨 principal/租户/参数的 continuation 被拒；修到全绿。

### Task 18: AIP-155 请求幂等（#195）

**Files:**
- Modify: `src/Schemata.Resource.Foundation/`（request_id 处理，复用现有 idempotency advisor 与原子缓存能力）
- Test: `tests/Schemata.Resource.Tests/`、HTTP/gRPC integration suites

**Interfaces:**
- Consumes: 规格 §4 U11 第三条；实现前从 `https://google.aip.dev/155` 读取 AIP-155 原文。
- Produces: 可配置兑现窗口（options，非状态位）与重复请求语义。

- [ ] **Step 1: 读标** — 读取 AIP-155 原文，提取 request_id 必须行为。
- [ ] **Step 2: 实现** — `request_id` 在请求消息上；提供即保证幂等；重复返回先前成功响应（不可行时可返回当前资源状态）；兑现窗口可配置；存储/并发复用 idempotency advisor + 原子缓存，不另设业务字段方案；提交 `feat(resource): honor AIP-155 request_id idempotency (#195)`。
- [ ] **Step 3: 测试（唯一一次）** — build 后 `--no-build` 运行 Resource.Tests 与 HTTP/gRPC integration；断言重复请求只有一次 mutation 且返回先前响应；修到全绿。

---

## Phase 4: 独立排期项（单独授权后启动，不在默认执行流）

### Task 19: #41 主动密钥轮换部分

- [ ] 授权后按 issue 正文与规格 §2.4 启动；验收以 issue 正文为准。

### Task 20: #74 双 origin 浏览器验证场景

- [ ] 授权且具备浏览器验证环境后启动：RP→OP iframe unchanged/changed、未知 origin 探测、cookie 限制观察；验收以 issue 正文为准。

---

## Phase 5: 集成验证与交付

### Task 21: 全量验证与交付

**Files:** 无（只读验证 + 授权后的远端操作）。

- [ ] **Step 1: 全量构建** — `dotnet build Schemata.slnx -c Release`，显式确认 0 错误。
- [ ] **Step 2: 全量测试（唯一一次）** — `dotnet test Schemata.slnx -c Release --no-build`；记录总通过/失败数；已知 flakes（Report.Integration.Tests net10.0、Authorization.Integration.Tests net8.0、SQLite/linq2db teardown 竞争）甄别，不作回归。
- [ ] **Step 3: §5.3 核对** — 逐项核对规格 §5.3 六条完成条件；未执行环境（browser/Redis/跨平台）单列说明。
- [ ] **Step 4: issue 台账** — 授权后：逐 issue 在 Gitea（cyp/Schemata）评论实际行为证据并关闭；延期项保持 open。
- [ ] **Step 5: 推送** — 授权后 `git push --force-with-lease github master`；推送前 `cog check` 全范围通过。

---

## 执行注意

- 共享文件（规格明示由集成 owner 维护的公共文件）冲突时，后到任务先 rebase 再改；禁止多个分支各自发明接口。
- 每个任务完成后按规格 §1 更新 issue 状态属于授权步骤，统一在 Task 21 Step 4 执行；任务内只提交代码与测试。
- 任务内 recon 发现实际 owner 与本计划 Files 不符时，以代码事实为准并在任务完成记录中说明偏差，不得为迁就计划新建路径。

---

## 执行状态与恢复指引（2026-10-04 暂停）

**进度**：Task 1–4 完成（规格 §6 的 Authorization、Resource/Insight、Identity、Scheduling 四个缺陷聚类），分支 `sdd/framework-remediation-2026-10-03` HEAD = `346271d7`（基于 master `83f9a4f2`，共 20 个提交）。下一步：Task 5（#166 Push 订阅保留）。

**工作区**：实现只发生在 worktree `D:\source\repos\Cyprin\Schemata\.worktrees\sdd-framework-remediation`；规格、计划、brief、台账在主检出（git 未跟踪，须随设备迁移）：

- `docs/superpowers/specs/2026-10-03-framework-remediation.md`（合同与验收权威）
- `docs/superpowers/plans/2026-10-03-framework-remediation.md`（本文件）
- `.superpowers/sdd/framework-remediation-2026-10-03/`（台账 progress.md + task-N-brief.md / task-N-report.md / 评审包）

**分支迁移**：分支尚未推送（push 需授权）。换机继续时二选一：授权推送 `sdd/framework-remediation-2026-10-03` 到 `github` 远端后在新机建同名 worktree；或整体复制仓库目录（含 `.git` 与 `.worktrees/sdd-framework-remediation`）。`.worktrees/framework-remediation-2026-10-03/` 是 harness 生成的无效投影目录，不携带、不使用。

**恢复步骤**：

1. 读台账 `.superpowers/sdd/framework-remediation-2026-10-03/progress.md`（ruling、deferred minors、每任务 BASE/HEAD）。
2. 按 `subagent-driven-development` 继续：Task N 的 brief 已备至 Task 10（task-5-brief.md … task-10-brief.md）；Task 5 的 BASE=`346271d7`。
3. 实现者串行派发，任务评审门后推进；Task 11–18 的 brief 届时从本计划对应节提取。
4. 每任务纪律不变：单 owner、DI 表达可选能力、禁状态位；build 一次后 `--no-build` 单次跑测；Conventional Commits + `cog check`；不提交 `.superpowers/`、`docs/superpowers/`；Gitea 只读。
5. 全部任务完成后进入 Task 21：全量验证 + 授权后的 issue 关闭与推送。
