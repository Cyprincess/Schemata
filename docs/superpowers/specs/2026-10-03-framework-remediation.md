# Schemata 框架修复与可靠执行规格（2026-10-03）

## 1. 目标、基线与交付边界

本规格是框架修复与能力建设的当前唯一输入；此前的审查报告、对照表与一致性工作稿已被本规格取代并删除。

- 代码基线：`83f9a4f2ac8c7e16108f89a74cbb3ccc59a6d0b6`（HEAD）。
- 证据基础：提交 `00c34ae07`（含）至 HEAD 共 70 个提交的静态审查与 HEAD 全项目审查；2026-09-25 对照覆盖的 #2–#152 issue 台账。
- 本文是内部设计规格，语言为简体中文；代码、配置、测试、提交与 issue 正文使用英文。
- 交付物：本规格与 Gitea issue 台账。实现、提交、推送分别需要后续授权。
- 验证边界：本规格的内容来自源码、LSP、历史 diff 与规范原文阅读，编写过程未运行 build、test、smoke；文中的验收条目是实施要求，不代表已执行。

### 1.1 范围选择

1. 范围 = 已确认缺陷 + 高优先能力 + VIP 反馈中可落地为框架能力的项。
2. 框架不提供任何 Outbox/Inbox 事务或投递能力；此类能力即使提供，也以 provider 形式存在而非 component 核心功能。内置 Scheduling/Event/Flow 的持久实体只服务框架内部审计与内部可靠调度。应用需要 inbox/outbox 或非标准投递格式（例如 webhook）时自行实现 provider、后端适配或自定义 advisor/handler。

### 1.2 固定架构决策

1. 一个技术职责只有一个执行 owner；transport、facade、后台桥接只适配或调用该 owner。
2. 可选能力由实际服务、handler、advisor 的 DI 安装表达；运行时通过 `GetService` / `GetRequiredService` / `GetServices` 及对应 keyed API 使用能力。
3. 协作型实现使用 enumerable；独占型 owner 使用单一默认或明确 keyed 选择。
4. 删除 `Enabled`、`Allow*`、`HasFeature`、空 marker 等第二套能力开关。事务 dirty/completed、缓存 lease、实体业务状态、不可重绑版本记录属于真实生命周期数据，保留其语义。
5. 干净切换所有仓内调用方；删除旧接口、空包、兼容别名、旧入口和重复 fallback；不提供内置数据迁移工具。
6. 资源 `Name` 由消费者提供；`Uid` 是严格唯一的内部实例身份；关联使用完整 canonical name。
7. 保留 `DictionaryKeyPolicy=SnakeCaseLower`。
8. 保留显式租户注册边界：host 服务由 host DI 构造；需要 tenant 构造依赖的服务显式 tenant 注册。
9. provider 层保留真实原子性、并发检查与事务语义；不以进程内锁模拟跨进程原子性。
10. 基线目标 `net8.0;net10.0`；generator 保留既有目标。

## 2. 范围与 issue 台账

### 2.1 当前缺陷（A 组）

来自 70 个提交与 HEAD 全项目审查。#184 已关闭为 #178 的重复，不在表中。

| issue | 标题 | 所属合同 |
|---|---|---|
| #153 | Authorization: align proof and assertion validator lifetimes with keyed scoped token stores | U2 |
| #154 | Authorization: include descriptor-owned authorization details in reusable consent admission | U3 |
| #155 | Authorization: enforce requested subject on the final interaction approval | U3 |
| #156 | Resource: preserve loaded system fields when mapping full updates | U5 |
| #157 | Resource: retain registered custom-method scope through gRPC and owner dispatch | U5 |
| #158 | Identity: gate dependent cleanup on an applied primary user or role deletion | U1 |
| #159 | Entity.EntityFrameworkCore: compare nested JSON snapshots by content rather than reference | R17 |
| #160 | Transport.Grpc: preserve the complete declared quota violation payload | R14 |
| #161 | Authorization: protocolize out-of-range JWT NumericDate claims | U2 |
| #162 | Authorization: preserve the validated RAR set through interactive authorization | U3 |
| #163 | Scheduling: commit one pending cancellation instead of a cancelled row plus stale retry | Scheduling 执行（与 U10 #188 相邻） |
| #164 | Actor: own disposal for activation instances replaced by supervision | U7 |
| #165 | Messaging.RabbitMq: release reply tracking across the full send boundary | R06 |
| #166 | Push: restore the retained subscription row when an address is recreated | R13 |
| #167 | Scheduling: carry the initiating principal through durable trigger dispatch | Scheduling 执行（与 U10 #188 相邻） |
| #168 | Scheduling: settle claimed executions after pre-body pipeline failures | Scheduling 执行（与 U10 #188 相邻） |
| #169 | Scheduling.Event: remove obsolete execution-interception configuration | R05 |
| #170 | Flow: arm outgoing timer catches when a token waits at an event gateway | R09；与 U10 #187 关联 |
| #171 | Flow: include active boundary hosts in signal broadcast candidates | R09；与 U10 #186 关联 |
| #172 | Flow.Bpmn: invoke activity bodies for every loop iteration | R09 |
| #173 | Flow.Bpmn: route called child snapshots through the transition owner | R09 |
| #174 | Flow: remove signal subscriptions with the same identity that armed them | R09；与 U10 #186 关联 |
| #175 | Flow.Bpmn: execute CallActivity through every automatic routing path | R09 |
| #176 | Flow.Bpmn: retain the actual boundary identity in durable compensation bindings | R09 |
| #177 | Flow: bound typed payload reachability traversal at registration | R09 |
| #178 | Flow: refresh working token snapshots between boundary deliveries | R09 |
| #179 | Insight: lower source aliases before repository filter and order compilation | U5 |
| #180 | Authorization: apply individual claim value qualifiers before destinations | U3 |
| #181 | Resource: consolidate optional stage control on DI registration | R05 |
| #182 | Report: run Insight plan advisors before report materialization | R08 |
| #183 | Docs: align module lifecycle examples with reflection-dispatched methods | 横向（Modular 文档） |
| #185 | Modeling.Generator: satisfy external interface contracts declared in entity bases | 横向（Modeling） |
| #145 | Contracts: consolidate new string constant holders under their component constants | 横向（Contracts） |
| #146 | Docs: make Authorization recipes respect independent resource names and current APIs | U2（文档） |
| #147 | Tests: replace marker and descriptor assertions with consumer-visible protocol outcomes | 全部单元 |

### 2.2 新能力（C 组，VIP 反馈处置）

| 编号 | 主题 | 处置 | issue / 合同 |
|---|---|---|---|
| S01 | 事件业务关联（Event/Flow.Event 订阅过滤与业务关联） | 采纳 | #186；U10 |
| S02 | Flow 定时器生命周期完整化 | 采纳 | #170 覆盖事件网关 timer 武装；取消/重建/身份部分由 #187 承载；U10 |
| S03 | 流程、业务数据与调度意图的共同提交 | 采纳 | 由 R01/R02/R10/R17 承载，不另建 issue |
| S04 | 持久事件 Outbox | 排除 | §1.1 第 2 条；应用自建 provider 或 advisor |
| S05 | 消息去重 Inbox | 排除 | §1.1 第 2 条；应用自建 provider 或 advisor |
| S06 | 可靠任务执行（lease、重试、恢复） | 采纳 | #188（执行存储 provider 合同，非核心功能）；U10 |
| S07 | Flow 外部副作用的持久执行边界 | 采纳 | #189（provider seam + 稳定 request_id；不支持去重/查询时上报结果未知，不承诺 exactly-once）；U10 |
| S08 | 框架内置推送 provider 实现 | 采纳 | #190（Web Push：RFC 8030 投递 + RFC 8291 消息加密 + RFC 8292 VAPID）；U10 |
| S09 | 通用 Webhook Transport | 排除 | #191 已关闭；webhook 非标准格式，由框架用户自行实现 provider |
| S10 | HTTP 消息签名（RFC 9421） | 采纳 | #192（transport 层通用能力，attribute 标记参与字段，覆盖 HTTP/gRPC）；U11 |
| S11 | 关系型原子缓存 Provider | 排除 | #193 已关闭；现有 Memory/Redis 全合同 provider 已提供原子能力，关系型后端可作为第三方 provider 包 |
| S12 | Resource 分页与调用作用域扩展 | 采纳 | #194；U11 |
| S13 | 请求幂等（AIP-155 request_id） | 采纳 | #195；U11 |
| S14 | 受控资料存储与端到端材料保护 | 排除 | 不新建对象存储/Vault 产品；后续按需单独提案 |

### 2.3 已关闭的高优先工作（U1–U8）

U1–U8 的实现已合入当前基线，对应 issue 均已关闭：#8、#15–#25、#43、#52、#72、#75、#76、#80、#82、#83、#89、#94、#99、#105、#106、#111、#116、#132、#135、#142、#143、#144、#148–#152。§3 中 U1–U8 的验收文本是这些行为的回归基线；与合同的现存偏差登记在 §2.1。

R01–R18 是持续有效的架构合同与回归要求，本规格不逐项声明其完成状态。与这些合同的开放偏差（#159、#160、#165、#166、#169、#172–#178、#181、#182）登记在 §2.1，修复以 §3 中对应合同文本为准；合同中未满足的验收继续作为实施要求。

两个例外：

- #74 open：check-session 的 header 修复已集成，其双 origin 浏览器验收场景（RP→OP iframe unchanged/changed、未知 origin 探测、cookie 限制观察）待执行。
- #41 open：仅主动密钥轮换部分，见 §2.4 延期。

### 2.4 排除与延期

- 排除：S04、S05（通用 outbox/inbox 产品）、S09 webhook（#191，非标准格式，用户自建 provider）、S11 关系型原子缓存（#193，现有 Memory/Redis 已提供原子能力）、S14（受控资料存储产品）、ID-JAG（#119/#121/#122 已撤销）、#104（维护者裁决接受 string max_age coercion）、mTLS。
- 延期：#27 Orleans、#36/#108/#109/#114/#118 CIBA、#38 recipient ID-token JWE、#41 主动轮换部分、#49 Rules、#53 BFF、#59/#61 持久额度、#100/#107 历史 OAuth flows、#101/#102 JAR 加密与外部 URI、#115 step-up、#123–#127 SCIM、#128 传输矩阵、#110/#112 证据交付。延期项保留 open 状态与原始验收。
- 低优先清理（延期）：#44 Actor 反射成本、#66 Skeleton Repository 依赖、#68 dispatcher 注册整合；随触及对应 owner 或有测量证据时启动，§5.1 的性能纪律同样适用。

## 3. 基线合同与回归要求

### U1 Identity 完整认证（#148–#151）

目标：以 ASP.NET Core 内置 UI/API 完整流程为行为依据，修复密码 → 第二因素 → 唯一签发路径；canonical subject 与平台 UserId 显式一致；共享 Refresh 校验票据 expiry；User/Role store 在实际 mutation+commit 边界转换并发错误。

验收：启用第二因素且无有效 remembered-client 证据的账户仅凭正确密码不能取得最终凭据；错误第二因素、锁定、记住设备、recovery 单次消费分别验证。最终 cookie/token 输出与 observer 只发生在完成的认证事件上。canonical subject 可经默认 `GetUserAsync`、security-stamp 和 Authorization subject 链找回同一用户。刚到期票据及缺失 expiry 拒绝。EF commit 和 Linq mutation 阶段的并发冲突产生同类 IdentityResult，普通 provider 错误不被吞掉。

### U2 凭据与 JOSE 消费（#116、#89、#41 当前消费链、#76）

目标：接受的 signing/encryption metadata 驱动真实签发与验证；UserInfo 选择当前合格 key，已撤销 inline key 不压过替换后的有效 `jwks_uri`；省略 ID-token signing alg 时默认 RS256 而不跟随任意 primary；当前验证需要的多把/retired 解密 key 在保留期内可用，主动签发只选当前有效材料；`client_secret_jwt` 与 `private_key_jwt` 按注册算法 pin 执行。主动轮换子系统不在本单元。

验收：用真实签名/加密结果检查 header、验签、解密和 JSON 类型。轮换后旧合法密文在合同保留期内可验证，新输出使用新 key；撤销 key 拒绝。客户端注册算法与请求实际算法不一致时失败。

### U3 协议响应与披露（#135、#142、#75、#80）

目标：PAR 错误固定走该 endpoint 的 JSON 错误合同，不经 RedirectUri/response_mode 投递成浏览器 callback；claims 解析、保存和 essential acr 检查归真实 claims 能力所有者，未启用时不执行；introspection details 按已认证 caller 的资源授权范围投影，不拿 token audiences 并集替代；RAR 续期保持结构化上界并支持语义缩权，本次缩权不覆盖原始授权。

验收：真实 HTTP PAR 在 query/form_post 等输入下失败仍为正确 JSON、状态和 headers，无 Location 或 callback HTML。多 audience token 由不同 RS introspect，各自仅获得合同允许的 details。RAR 缩权/扩大/省略与 typed response 逐项验证。

### U4 客户端删除 fence（#111）

目标：普通签发、family 后继与管理凭据轮换纳入同一 application 失效事实的消费范围；DELETE 的成功边界与凭据发布成败一致。由凭据发布/验证的既有持久 authority 表达失效边界，不引入跨库全局锁，不以更多次 drain 充当原子性。现有 provider 不能满足某原子边界时明确不支持该配置，不用本地锁模拟。

验收：屏障控制签发在加载 app 后暂停，完成删除后再允许发布；不得返回随后可用的凭据。覆盖 PUT/RAT 轮换、普通 create 与 family successor 竞争。删除前已有凭据和迟到发布都按合同失效；其他应用不受影响。

### U5 Resource/Insight 授权与估算（#143、#52、#43）

目标：Create 在 instance access 前确定实体的结构性 parent，授权看到的对象与落库对象一致；Insight 建立公开字段合同，在 default projection、显式/嵌套 path、filter、join、aggregate、residual 中统一执行；EF 估算经过真实命令拦截边界，provider 无法安全执行时明确不提供估算，不伪造 interceptor 通知。

验收：跨 parent 的 Create 在错误 parent 上允许、真实 parent 上拒绝时仍被拒绝；允许路径落库 parent 与授权对象一致。private 字段不能通过投影读取，也不能通过 filter/join/residual 推断。安全相关 EF interceptor 能看到/拒绝估算执行，取消和失败不残留 SHOWPLAN session 状态。

### U6 原子缓存（#22、#24）

目标：普通缓存、条件原子写与集合能力只由真实可执行的实现提供；普通 `IDistributedCache` 不再用进程内锁声称跨进程 CAS，迁移全部安全/幂等消费者，不保留不原子的 fallback；修 Redis `SREM → SCARD=0 → DEL` 并发删除新成员的窗口；保留 generation 初始化胜者、commit 换代和晚填旧代际合同。关系型后端不作为框架工作项，可由第三方 provider 包接入同一入口。

验收：两个独立 runtime/client 同时保留同一 key 只有一个胜者；replace/remove 比较实际预期值；受控 interleaving 下集合删除不吞并发 add；当前代际不被旧查询晚填恢复。Memory 测试不冒称分布式证明；Redis 跨进程场景使用真实隔离目标，缺席时保留明确未执行记录。

### U7 Actor 终态与宿主停止（#132、#25）

目标：cancellation callback 抛错时仍执行终态通知/资源清理，确保一次 terminal transition，不提前释放仍运行的同 identity slot；接入 host stopping：关闭新获取/投递准入，通知当前 activation 停止并等待真实完成，使用宿主给定取消预算；不合作 callback 超过预算时报告实际限制，不承诺强制终止任意托管回调。

验收：初始化失败、turn 异常、取消回调异常、OnStopped 异常和并发 Stop 都进入一致终态；host 停止后拒新工作，已运行回合按预算排空/中止，未结束的旧执行不与新同 ID 执行重叠。本单元不含逻辑 ref 自动唤醒、idle collection、Orleans 或 timers。

### U8 租户隔离纵切（#15–#21、#23、#24）

#### U8.1 HTTP 两阶段解析（#15、#16）

目标 middleware 顺序：ForwardedHeaders/HTTPS/诊断 → 结构化错误处理 → CookiePolicy → Routing → CORS → Request 阶段租户解析 → Authentication → Principal 阶段核对/绑定 → Authorization → RateLimiter → Session → Endpoint。

- `ITenantResolver` 增加 `Stage`（Request/Principal），保留 `ResolveAsync`；所有内置与自定义 resolver 明确阶段，同类型通过 `TryAddEnumerable` 去重，多个不同 resolver 同时运行。
- Request 阶段执行 host/header/path/query。未提供来源返回 null；malformed 和明确 ID 指向未知 tenant 都终止请求。多个非空 ID 必须一致，注册序不用于覆盖冲突。Host 无匹配返回 null 让其他来源参与。
- 仅 Principal 阶段读取已认证主体；未认证 principal 的 tenant claim 不可信。
- Request 阶段有 tenant：认证前进入其身份并建立 tenant scope，认证从该 scope 解析服务。无 tenant：宿主 scope 完成认证，Principal 阶段可绑定新 tenant scope，后续业务只复用验证完成的 principal。
- 两阶段 ID 冲突立即失败，不换 scope 后继续；无来源保持宿主身份。
- Core 分离 Authentication 与 Authorization 插入点；Transport.Http 结构化错误 middleware 移到外层，删除原位置重复安装。
- 请求取消的 `OperationCanceledException` 透传并释放 scope/身份；非取消异常正常映射。

验收：五 resolver 同时注册；相同值成功、冲突/格式错误/未知明确 ID 拒绝、无来源进入宿主。路径来源在路由匹配后可读；认证服务实际看到 Request 阶段 tenant；principal-only 的宿主认证与 tenant 业务 scope 可区分；冲突后无 handler 执行。HTTP 错误具有既有结构化错误体，取消不转普通 500。

#### U8.2 不可变身份与执行 scope（#18、#20）

- `TenantIdentity` 放在 Abstractions 的执行上下文合同，值为 tenant ID 或显式 host；实体快照是不同对象，不放进身份键。
- AsyncLocal 保存不可变 frame；同步进入返回恢复租约，`finally` 恢复父 frame；并行子执行各建 frame，不共享可变 accessor；无 frame 表示 host。异步工厂内部的 AsyncLocal 赋值不作为向调用者传播身份的机制。
- 中立 `IMessageExecutionScopeFactory.CreateAsync(MessageContext?, CancellationToken)` 位于 Messaging.Skeleton，返回准备完成的 `IMessageExecutionScope`：Services、不可变 TenantIdentity、同步 Enter 和 DisposeAsync。
- 调用方生命周期：await 准备 → 同步 Enter 身份 → 恢复其他 propagator → 解析 handler/执行 → 退出 frame 租约 → 异步释放 scope。Enter 租约属于调用方；工厂为权威 bootstrap 查找建立自己的临时 frame，并在返回前恢复。
- 默认实现准备宿主 scope；Tenancy bridge 先解析身份及权威快照，再创建最终 tenant scope。工厂失败不执行 handler，沿接收入口既有错误协议返回，不增加自动重投。
- 该合同替换 `IActorTurnScopeFactory`、默认实现和 `TenantActorTurnScopeFactory`；Actor、RabbitMQ 及实际跨 scope 的 Event/Job 入口一起迁移。Actor 传入激活绑定身份的 context，入站身份不一致时拒绝。Job 使用持久化执行身份，不继承上一任务 frame。
- Actor runtime 在 Enter 后、每个用户回调前单独建立 `ActorTurnIdentity`，退出时恢复；普通 RabbitMQ/Job handler 没有 Actor 回合标识。
- 宿主 singleton 的依赖图不因换 frame 自动重建；应用 singleton 遵守调用时身份合同或显式注册 tenant override。

验收：`CreateAsync` 真正异步挂起后，Enter 内可见准备的 tenant，退出恢复父身份；嵌套、异常、取消和并行执行互不污染。handler 构造使用最终 tenant 服务。Actor lifecycle 与回合身份相同；工厂失败释放已建 scope。

#### U8.3 本地管理与权威版本快照（#17、#19、#21）

- `SchemataTenantManager` 编排本地租户 Handler；Handler 移除 Messaging request 接口，保留单一业务实现。resolver 的本地查找不经出站 dispatcher。Tenancy.Foundation 删除 Messaging 引用、command/query envelope 和 dispatcher 安装；独立桥可公开远程操作，但不改变本地管理语义。
- SetDisplayName/SetDisplayNames 赋值后执行 repository Update 和 Commit，空 map 规范化保留。SetHosts 的删除、增加和所属 tenant 版本更新属于同一 UoW。Create/Update/Delete 各自一次提交；commit 失败不失效 provider。
- provider cache 键使用 `(TenantIdentity, Timestamp)`，复用 `SchemataTenant` 已有 Guid 并发令牌，不增加第二版本列。创建产生非空 Guid，每次成功实体更新在同一事务内更换；失败/回滚不发布候选版本。
- 每次新执行 scope 通过既有本地 manager/repository 查询从权威存储读取 tenant 和 Timestamp，绕过 Entity.Cache 与 provider cache；一次读取提供同版本实体。SetHosts 同事务更新 tenant Timestamp。
- commit advisor 仅退休本进程旧版本 provider；旧租约归零后才销毁。删除 Handler 散落的 cache.Remove。`ITenantProviderCache` 迁移组合键，保留现有并发首建/引用计数/退休控制。
- 新 scope 读取删除后的权威状态时失败；已在途 scope 保持原快照到请求/回合结束。即时撤权由业务操作边界检查权威状态。滑动过期只负责资源回收，不承担失效正确性；多节点读取同一权威 Timestamp，不依赖可能丢失的 pub/sub。
- 直接 SQL 等外部写入必须同步更换 Timestamp；EF Core/LinqToDB 的并发 token、回滚、重试语义一致，不发布未提交的 CLR token。

验收：setter 成功后独立 scope 可读新值；失败不改存储和 provider 当前版本。绕过管理门面但经过 repository 的 mutation 也更换版本；SetHosts 与 tenant 更新共同回滚。跨节点新 scope 读取新版本，旧请求持有旧 provider 到租约结束；持续访问不能延长旧版本对新 scope 的可见性。

#### U8.4 缓存命名空间（#24）

- `Schemata.Tenancy.Caching` 给实际可用的 cache 能力提供同一身份 namespace；所有 Get/Set/Remove、CAS、集合名采用版本化、长度分帧 `(host-or-tenant, tenant-id, original-key)`，避免简单拼接碰撞。
- Entity.Cache 保留 provider/连接身份、SQL/参数/投影结构、terminal operation、result type 的现有 key 信息，外层应用 tenant namespace。generation 按 `(tenant, entity type)`；A 提交不能清空 B 的 generation。查询捕获的 generation 不变，晚完成的旧查询仅填旧 generation。
- Repository 可以共享；查询过滤仍由应用提供。缓存 namespace 不替代行隔离，行过滤不替代普通 cache namespace。Entity.Cache 复用同一 cache view，不另建第二套租户 cache key 规则。

验收：A/B/host 同原始 key、CAS key、集合名互不影响；含分隔符的 key 不碰撞。两 tenant 相同 provider/SQL 不共享结果；A commit 后 A 新查询不命中旧结果，B cache 不受影响。

### U9 框架一致性（R01–R18）

固定选择：

| 主题 | 目标合同 |
|---|---|
| 缓存接口 | 全能力 `ICacheProvider` 统一普通、原子和集合操作；删除 `IAtomicCacheProvider`、`ICacheCollectionProvider`；不能实现全合同的 Distributed 适配退出框架 provider 体系 |
| 资源写入 | `IResourceMutation<TEntity>` 是所有 AIP resource 语义写入的共同边界；默认 repository 实现与领域实现接管同一位置 |
| 事务 owner | 自提交操作拥有事务；参加外层操作的 mutation 显式接收同一 UoW，只 stage/enlist，由外层提交 |
| 提交后领域行为 | resource mutation 拥有 pending events、Report 调度同步等领域副作用；原始 repository 不再自动执行这些行为 |
| Repository 开销 | 删除通用 entity change snapshots 和三份 entity 跟踪列表；保留类型级缓存失效、provider 不变量和 UoW 生命周期 |
| Report 扩展 | 标准 `IReportService`/`IReportSnapshotStore` 为受守卫入口；`UseService`/`UseSnapshotStore` 安装 keyed 内层实现 |
| Owner 包 | ownership 合同与实现并入 `Schemata.Entity.Repository`，删除 `Schemata.Entity.Owner` 项目 |
| 包分层 | 保留真正隔离 backend/transport/邻接 runtime 依赖的 provider、Http/Grpc、Tenancy 桥；合并不把 transport 依赖拉入 Skeleton |

#### R01 统一 resource mutation 与事务归属

1. `IResourceMutation<TEntity>` 放在 `Schemata.Entity.Repository` 层；删除 Abstractions 旧定义并完整迁移 namespace/reference。默认开放泛型实现位于 Repository，领域闭合实现替换同一 owner，复用事务编排与 R02 后置处理。
2. Create/Update/Delete 接收显式可选的既有 `IUnitOfWork` 参数。未传事务时 mutation 解析 repository、调用其 `Begin()` 并拥有提交/回滚/释放；传入事务时实际参与的 repository 先 `Join(uow)`，mutation 只 stage/enlist。不能从 repository 是否已有事务推断提交权，不引入 ambient UoW。
3. repository、UoW 和 operation-scoped suppression 属于实际执行写入的同一调用路径。Expunge 的物理删除意图通过显式 mutation 操作参数传递，由 mutation 在自己的 repository 上建立并恢复 suppression。
4. Resource handler 保留 mapping、policy 和 response shaping；持久化一律解析 `IResourceMutation<TEntity>`。Undelete/Expunge/Purge 保留各自命名动作、权限和状态约束，以明确操作意图复用该 owner。
5. ApplicationManager 与 Identity User/Role 的领域写入各有唯一当前 mutation owner。Identity owner 放在 store 和资源入口均可依赖的层：`IUserStore`/`IRoleStore` 标准方法作为不传 UoW 的自提交适配器，资源入口以显式可选 UoW 调用同一 owner；owner 不回调 store，不存在绕过它的第二条写入路径。IdentityResult/并发异常转换保留在适配边界。
6. 统一公开事务入口为 `Begin/Join` 与 mutation 的显式 UoW 参数；删除 `IRepository.GetWriteUnitOfWork()` 及以它猜测提交权的调用。
7. UoW 保留一次性合同；自提交 store 的连续独立调用使用新的 repository/UoW；原子 CAS 重试只复用现有明确的 fresh-scope 路径。删除无实际消费者的 `finalize=false` 分支。
8. repository 单实体写入返回值类型结果 `Applied`/`NoWrite`。`Applied` 表示本次调用成功 stage 或执行了事务内写入，尚不等于 durable commit；异常保持异常通道。结果必须来自本次操作，不能从 UoW 存在、全局 dirty 或 outer commit 成功推断。
9. 默认 provider 用实例级单调写入序号记录成功 staging，并比较一次调用前后值；序号在 provider staging/SQL 成功后推进，不在 advisor 返回 Continue 时提前推进。`Block` 不得 stage；无写入的 `Handle` 返回 NoWrite；软删除 `Handle` 内部调用同一 repository 的 Update 并传播实际写入结果。写入其他实体不等于该请求 Applied。Block-after-write 属于 advisor 合同违例，不能按正常无写入返回。
10. mutation 将实际写入结果与资源、操作意图交给 R02。Delete 经软删除 Update 完成时仍是资源 Delete；领域 owner 的级联资源写入分别通过对应 mutation 参加同一事务。
11. before-commit publication fence、repository 基础不变量及 CAS 原子性保持。持久化失败与提交后动作失败分开处理：后者不能声称已回滚数据库，也不自动重试已提交 mutation。

验收：HTTP/gRPC、本地 manager 和后台资源操作对同一实体得到相同生命周期结果，无 raw fallback 或 manager/mutation 循环。自提交成功后可执行下一次独立操作；外部事务中的两个 mutation 只在一次 outer commit 后可见；子操作失败不自行 rollback 调用者事务；outer rollback 后所有写入不可见。Identity stale stamp 仍转换为既有 IdentityResult/规范异常。同一外部事务包含 Applied、Block、无写入 Handle 时仅 Applied 注册领域后置处理。Expunge 在 mutation 实际使用的 repository 上物理删除；嵌套 suppression、异常和取消后原状态恢复。

必须迁移的入口类别：Resource 标准 Create/Update/Delete 与 allow-missing Create；自定义 Undelete/Expunge/Purge；Identity User/Role；Authorization Application/Scope/Authorization 管理与 DCR；Push subscriptions、Report definitions/snapshots；Tenancy 管理；Flow process/token/source/transition/compensation/participant 与 Scheduling job/execution（外层领域操作拥有跨资源事务，各资源写入参加该事务）。实施时对所有生产 `AddAsync/UpdateAsync/RemoveAsync/CommitAsync` 调用归类，不能仅迁移 `Schemata.Resource.*`。

#### R02 移除 repository entity snapshots，重划提交后职责

1. 删除 `CommitChanges<TEntity>`、`SnapshotChanges`、`TrackAdd/TrackUpdate/TrackRemove`、`HasPendingUpdate`、基类三份 entity 列表及快照复制。R01 的标量写入事实承担 Join 未提交工作检查；R17 的 active enlistment 承担事务缓存 gate。保留 provider 为实际批量 SQL 所需的局部 materialization，不当通用 change journal。
2. 保留同名 `IRepositoryCommittedAdvisor<TEntity>`，改为仅含 repository 参数的类型级通知；该 repository 实际写入且 UoW 持久提交成功才执行；空事务/纯读不触发；仍以 enumerable advisor 注册。
3. query cache eviction 消费类型级通知。原始 repository 写入、协议原子写入和跨资源事务仍使相关类型缓存失效；rollback 不失效。删除 `EvictionEnabled` 运行时权威，以实际安装的 advisor 表达能力，保留单次受控 suppression。
4. mutation 后置 advisor 安装在 resource mutation 路径，接收该 mutation 的实体与领域操作意图。只有 R01 返回 Applied 且存在实际后置消费者，才捕获所需引用并向本次 UoW 登记 callback；NoWrite 不登记；outer rollback 丢弃 callback。不向 `AdviceContext` 放 entity、事件或通用写入 sink。
5. `Schemata.Entity.Event` 保留为实际的 mutation/Event 依赖桥；pending events 在成功提交后的 callback 中才 dequeue/publish，同一实体缓冲中的事件只 drain 一次；原始 repository 不再隐式 drain `IHasPendingEvents`。原 `UseEvent` 的安装改为 mutation advisor，删除旧 repository event advisor 路径。
6. Report 调度同步作为 Report mutation 后置处理，按 R08 的资源操作矩阵执行；原始 repository 不再隐式 arm/disarm。
7. 在现有 `IUnitOfWork.AddCommitSink` 增加顺序键，注册时稳定有序插入，同键按登记次序运行。repository 类型级通知 sink 使用早于 mutation 领域处理的顺序段；各段内部保留 advisor 的 Order 排序。**同一 UoW 的全部类型级失效先执行，再执行任何 mutation 领域后置动作**，与 repository/mutation 交错登记次序无关。显式迁移其他 sink 调用方，不另建提交阶段管理器。
8. 两种 provider 保留提交后 callback 全部尝试、收集并传播错误的语义。失效失败不改写为成功，后续动作仍按此失败合同执行；不能宣称失败场景仍保证缓存新鲜。数据库已提交时保持新并发 stamp，不执行失败保存的恢复，也不重放整个 mutation。rollback sink 保留既有生命周期用途。
9. callback 存储按首次使用延迟分配。能力选择来自当前 scope 的实际 advisor 集合，不新增 `HasCommittedObservers` 等缓存布尔状态。

EF 并发 baseline 与 stamp 时机：

- EF Update staging 不旋转 `IConcurrency.Timestamp`。未跟踪实体以调用者值 attach；已跟踪非 Added 实体在每次显式 Update 时用当前传入 timestamp 设置 `OriginalValue`，并保持 full-update/touch 的 Modified 语义。不能只保留加载时数据库 token，否则“加载后传入 stale token”会被误接受。
- 同一实体重复 Update 在提交前仍持有同一传入 stamp，不 detach/re-attach，不维护 provider 专用 entity HashSet/dictionary。Added 实体由 add 路径拥有初始 stamp；相同 key 的不同实例保留 EF 跟踪冲突边界。
- `EfCoreUnitOfWork` 在 `SaveChangesAsync` 前用现有 `ChangeTracker` 遍历 Modified 且配置为 concurrency token 的 Timestamp 属性，生成待写入 stamp。保存失败时在现有 entries 上将 CurrentValue 恢复为本次传入的 OriginalValue，再执行 rollback 生命周期；不为恢复复制实体集合。成功后返回的实体 stamp 等于持久化值。
- 可观察时机改变：EF 实体在 Update staging 后、commit 前保留调用者 stamp；新 stamp 在成功 commit 后可用。LinqToDB 继续在事务内执行 SQL 时旋转并刷新；两个 provider 对提交后的值、stale rejection 和原子性保持一致。

验收：纯读、无后置消费者、普通单实体写入均无基类 entity tracking list/dict 和 snapshot copy；empty commit 不更新缓存 generation。outer commit 包含两个实体类型时，A 的事件处理器查询 B 读到新值；调换 enlistment 顺序结果相同。单个 sink 失败时其余 sink 被尝试，调用者收到错误且能独立查询到已提交数据。Applied 的 pending events 成功提交后发布；Block、无写入 Handle、outer rollback 不发布；同一 entity 多次 mutation 不重复 drain。EF 覆盖首次 Update、加载后传入 stale stamp、无字段变化的 touch、同实例重复 staging、不同实例同 key、跨 commit、保存失败后恢复与重试、stale Delete；提交后重载 stamp 与返回实体一致。对相同数据、provider、TFM 测量纯读/单写/批量写的 allocation 和 throughput，记录原始值，不预填提升比例。

#### R03 合并 ownership 包与收敛 suppression 生命周期

- `IOwnerResolver<TEntity>`、`SchemataOwnerOptions`、`OnNullOwnerPolicy`、owner mutation/query advisors 和 suppression 扩展迁入 `Entity.Repository`。
- 引用格式/存在性校验继续位于 Repository，独立于 `UseOwner()`；`UseOwner()` 只安装实际 ownership advisors，保持显式启用。
- 删除 `Schemata.Entity.Owner` 项目、solution 条目、所有项目引用、旧 namespace/re-export；更新 Authorization/Flow/Push 和测试消费者。
- `ProcessPersistence.SuppressOwnerQueries` 改用现有 `AdviceContext.Use<T>()`，scope 覆盖实际 work 与 commit，成功、异常及取消后恢复原值；不在 helper 返回前释放。

验收：引用校验在未启用 Owner 时仍运行；启用后查询与赋值遵循原 owner 策略。Flow 操作结束后同一 repository 的普通查询不继承内部 suppression，嵌套 scope 保留外层状态。仓内不存在 Owner 项目引用。

#### R04 全能力缓存接口与单一后端绑定

1. `ICacheProvider` 包含现有 Get/Set/Remove、TryAdd/TryReplace/TryRemove 和 collection 方法；删除两个能力接口及所有分裂消费点。
2. Memory 与 Redis 实现全合同；Redis 条件操作在共享 backend 内跨客户端/进程原子执行；Memory 只承诺共享实例范围原子性。
3. 删除 `Schemata.Caching.Distributed` 项目及其框架注册扩展；应用自有 `IDistributedCache` 用法不动。
4. concrete backend 在内部 keyed `ICacheProvider` slot 安装；public `ICacheProvider` 是同一后端的唯一规范出口。重复选择采用最后一个显式 provider 选择，重复相同选择不增加出口或包装层。
5. `AddMemoryCacheProvider`、`AddRedisCache` 和第三方 provider 注册均经过这一入口；多个独立 backend 只能显式按 key 选择；单个操作的普通/原子/集合访问不跨 key/backend。
6. `AddTenantCache` 通过 public 出口到 keyed backend 的稳定组合安装，先后顺序与重复调用不改变语义；删除 descriptor-last `Decorate` 及三个能力专用 wrapper，统一为一个全能力 tenant wrapper。
7. 框架支持的注册入口在 `GetServices<ICacheProvider>()` 中只暴露已选规范出口，不遗留 unkeyed raw backend。应用直接替换公共服务意味着接管完整合同。
8. caller 缺少必需 backend 时第一次解析/使用暴露 DI 错误；不得静默退回未隔离或非原子 provider。

验收：不同注册顺序、重复注册、Memory/Redis 切换均对同一 key 产生可互见的普通、原子、集合结果。跨 tenant 相同 logical key 隔离；scope 恢复到 host 后也隔离；public scalar/enumerable 解析均不泄露 raw backend。CAS 竞争、TTL、集合增删与 clear 使用同一数据状态。Redis 跨进程场景使用真实隔离测试目标。

#### R05 可选功能的 DI/advisor 安装

- Resource missing-stage 授权服务按 entity 闭合或 keyed 安装；调用方通过服务是否存在决定执行，删除 `IsAuthorizationActive` runtime probe。
- DCR Replace/Delete 分别通过实际 handler/endpoint 安装选择；删除 `DynamicRegistrationOptions.AllowUpdate/AllowDelete`。未安装的操作在 HTTP 路由和本地 dispatch 上一致不可用。
- `IAuthorizationFlowFeature.Actions` 的重复字符串目录迁移到实际 endpoint/handler 安装事实，路由与 discovery 消费相同结果。
- Actor persistence 从最终 turn scope `GetService<ActorStateStore>()`；删除 IsService→manual new 路径；tenant 构造依赖由显式 tenant 注册提供。
- `WithoutCreateValidation/WithoutUpdateValidation/WithoutFreshness` 改为选择安装的实际 advisor；request 的 ValidateOnly 和单次受控 suppression 保持各自行为。
- 协作 validators 用 `TryAddEnumerable` 注册每个实现，`AdviceValidation<T>` 用 `GetServices<IValidator<T>>()` 汇集错误；实现多个 `IValidator<T>` 的类型注册所有闭合接口；相同实现重复安装不重复执行。
- Identity/Authorization 可选服务采用一种解析方式；删除 optional constructor 参数后再次解析同一服务的 fallback；可选 session capability 缺席时按既有无 session 路径工作，不新增默认 no-op 服务。
- `AdviceAuthorizeClaims` 的批准校验归入既有 advisor 阶段或共享纯校验逻辑；不能同时将 concrete advisor 注册成能力 marker 再旁路调用。
- Modular 的后续生命周期通过已选 DI runner 解析；配置期已执行的 runner 与运行期是同一个 owner；删除 `Committed` 私有实例双权威；不允许重复 ConfigureServices 副作用。

验收：安装/未安装、重复安装、feature 次序、closed/keyed/custom 实现组合产生相同可见能力；禁用一实体授权不影响另一实体。所有协作 validator 执行一次；dry-run 不写入。Actor 缺 persistence 时不访问存储，安装时使用容器提供的实例和依赖。

#### R06 tenant dispatcher 与 Event 协作集合

- tenant messaging 的 concrete dispatcher 与公共接口 alias 绑定同一选定 owner；保留 RabbitMQ outbound 与 inbound in-process 的区别。
- unkeyed enumerable 的 host+tenant 合成保持原顺序，独占 handler 多注册继续明确失败；协作 advisors/propagators 组合不丢项。
- Broadcast 将 typed handlers 与 catch-all subscribers 合为同一去重集合；Flow.Event 桥接器不因新增 typed handler 而失效。CompetingConsumers 仍选择一个实际候选。
- `HasHandlers` 与实际调用复用同一候选计算，避免探测与执行选择不同集合或构造两批 transient handlers。

验收：tenant 消费者解析 dispatcher 接口后执行 tenant handler/advisor；host 保持 host 行为。注册普通 typed event handler 或 Actor.Event forwarder 后，同一事件匹配的 Flow subscriptions 仍处理一次。无 subscriber 发布保持成功；消费侧缺 handler 的配置错误仍传播。

#### R07 统一资源注册结果并封存

1. `ResourceRegistry` 作为唯一注册 owner；合并后的 descriptor 同时用于 DI wiring、授权安装和 HTTP/gRPC metadata。
2. 同实体重复注册的 entity/request/detail/summary tuple 必须相同；不同 tuple 在注册调用处明确拒绝。
3. endpoints 先复制到 registry 自有可变集合再 union；不修改调用方数组/list。未限制 endpoints 与显式限制按既有“全量优先”规则合并。
4. scheme、paging 和操作策略：未设置可由后续显式值补全；两个冲突的显式值在注册调用处拒绝。不能一侧 first-wins、另一侧实际安装 second-wins。
5. 首次 runtime 读取发布不可变 snapshot；Resources、methods、endpoint/operation 集合和属性不暴露可写内部对象；调用者继续修改原输入不影响 snapshot。
6. 修复 builder 与 Foundation 中重复创建 registry 的路径，使两者复用同一实例和注册语义。

验收：Http/Grpc 双注册、数组 endpoints、重复相同配置、后补 scheme/paging 均得到一致结果；冲突不留下部分 DI 闭包。封存后的集合/属性不可改写，enumeration 与 direct lookup 不分叉。

#### R08 Report 的一个守卫入口与完整关联身份

- `IReportService`/`IReportSnapshotStore` 标准接口解析到统一守卫入口；调用方不再依赖具体 facade 类型。
- 既有 builder 安装 keyed custom inner；删除 `OwnSlot<T>` descriptor 清理。任意直接替换 outer 接口意味着应用接管其合同。
- Report 选择只保留一份实体 triple 注册结果，冲突在实际能力调用时拒绝；删除 `ReportSelection.Conflict` 与 `ReportRegistration.HasConflict` 两份状态，以及必然 no-op 的第二次 TryAdd。
- 相同 triple 合并配置并仅安装一组 handler/source/job；冲突不安装混合闭包。Build 可以成功，Report.Scheduling StartAsync 的实际 periodic 枚举仍允许暴露 FAILED_PRECONDITION，不静默跳过。
- facade、handler、resource advisor 的守卫归到覆盖各公开入口的最早执行边界；默认路径不重复检查，自定义 inner 和 raw dispatch 均不能绕过规范入口的 guard。
- chunk 查询解析完整 snapshot canonical name，并同时约束 Report 与 Snapshot；GetChunkAsync、ReadRowsAsync、分页和 retention 使用同一关系键。

调度生命周期（消费 R02 的资源意图）：

| 资源操作 | 提交后的调度行为 |
|---|---|
| Create / Undelete | 活跃且周期执行的 Report arm；其余保持无周期 job |
| Update | disarm 原配置；最终状态活跃且周期执行时 rearm |
| Delete / Expunge / Purge | disarm；软删除底层执行 Update 也不得 rearm |
| NoWrite / rollback | 不执行同步 |

同一 Report 在一次 UoW 中多次 mutation 时按登记顺序应用语义，最后一个成功操作决定最终调度状态。arm/disarm 使用既有资源关联键避免重复 job；重复更新相同周期配置和 startup initializer 重建不产生多个活动调度。调度 job 写入属于提交后的独立操作，不宣称与已提交的 Report 数据拥有跨事务原子性；失败遵循 R02 的错误传播合同。

验收：冲突下 standard CRUD、自定义方法、snapshot store、generation/job/retention 在业务 I/O 前失败；同 triple 重复无重复执行。`reports/A/snapshots/daily` 与 `reports/B/snapshots/daily` 数据互不混入。服务启动后新建周期 Report 无需重启即可调度；Create→Update、Update→Delete 与 rollback 的最终 job 状态正确。

#### R09 Flow 任务绑定复用 Expressions 与冻结合同

- ScriptTask 的表达式计算使用现有 keyed `IExpressionCompiler`、类型化输入/结果和现有表达式缓存；删除另一个通过枚举 `.Language` 查找的框架语言目录。
- BusinessRuleTask 绑定应用提供的明确 rule handler；规则动作的 version/key 属于被固定的 process definition。不引入完整 Rules 产品，不用测试 fake 代替生产表达式适配。
- 任务工作在 Flow 外层事务中调用资源 mutation；引擎只负责执行和状态推进，提交由 Flow owner 负责。
- output mapping 是冻结图的一部分，注册后不可经字典 getter、原输入别名或集合操作改变。
- 输出缺字段、类型不符、未知语言/handler、NoMatch.Fail 均在操作时失败；NoMatch.Continue 按既定不产生输出的结果自动推进。异常不得留下部分 process/token/source/transition 数据。
- 版本 unregister 后仍禁止把已使用版本绑定到不同定义；不删除 `_registeredVersions` 的防重绑定语义。

验收：真实 CEL 表达式与应用 rule handler 执行后自动推进，后续条件可读取结果；输入/输出类型错误和 rollback 通过真实持久化场景验证。Script/Rule 输出集合冻结后修改被拒绝或仅修改调用者副本，历史实例使用原定义。

#### R10 Token store 连续操作与 replay cleanup

- `RepositoryTokenStore` 的自提交公开操作必须可连续调用，不把内部 Begin/Join 的 completed repository 留给下一次公开调用。
- `InvalidateFamilyAsync → RevokeByAuthorizationAsync` 和 lost-redemption→revoke 路径使用同一明确的操作事务或现有 fresh-scope store 机制；关闭的 repository/context 不继续使用。
- 缺少 family marker、已 invalidated、CAS loser、回滚与成功各路径均保持后续操作可用。
- replay rejection 前的必要撤销全部完成；结果仍为既有 `invalid_grant`。真实持久化错误独立传播，不能用捕获 ObjectDisposedException 后返回 invalid_grant 掩盖。
- publication fence 仍由 Application mutation owner 在实际 write transaction 中 enlist；撤销/删除不是新 publication，不为对齐方法形状附加无意义 fence。

验收：真实 EF Core/LinqToDB 上验证带 Family+Authorization 的 replay、并发兑换 loser、marker 缺席/已失效、连续存取；断言派生凭据不可再用和正确协议错误，而非只验证 mock 调用次数。

#### R11 生命周期安全的流式读写

- Purge force、User RemoveClaims、Role RemoveClaim 采用读取前显式共同 UoW，或有界 materialization 后写入；不得在活动 reader 中首次切换 repository context。
- 保留 repository 的查询过滤与 mutation advisors；批量路径不得直接 SQL 绕过已声明资源生命周期。
- Purge 的 preview 与 force 分别验证；force 必须执行真实 provider 写入，不能仅以 mock 或 preview 证明可删除。

验收：0/1/多行匹配、第一行/中间写入失败、取消、软删除与物理删除均不访问已释放 reader/context；失败按操作原子性合同回滚，无半完成 claim 删除或 purge。

#### R12 tenant provider lease 与清理

- retired-provider 清理失败不能丢失已增加的 lease count。先完成必要清理再交付，或在交付失败时释放本次获得的 lease；发布 entry 与交付 handle 的异常路径成对。
- DisposeRetired、DisposeProviders、DisposeAsync 对全部候选执行清理，然后聚合失败；失败不能使已移出索引的未处理容器永久失联。
- 保留 Disposing guard 与 lease 最后释放后处置语义，不误删为功能状态位。
- 使用既有 CompositeScope 的全成员清理/异常传播规则，不另造容器回收框架。

验收：一个 retired provider Dispose 抛错时其他 provider 仍被释放；新 lease 要么交付且可释放，要么计数撤回；Remove/容量淘汰/TTL/host shutdown 都不泄漏活动计数，不重复 dispose。

#### R13 Push 共享控制合同与正常宿主安装

- Push.Http 使用既有 Schemata feature/application-part 机制安装 controller；正常 `UseSchemata` 宿主与 plain MVC 都有明确支持的安装路径。
- HTTP/gRPC 共享 owner 解析、权限名称、输入校验、target 默认值、options 与每 transport outcome 语义。消息保持任意合法 JSON 值；gRPC wire 用明确 JSON payload 字段适配到与 HTTP 相同的值，不扁平化覆盖 title/body/data。
- priority 缺席使用 `PushOptions.Default` 的 Normal；未知 target discriminator 拒绝，不能广播。缺席 target 保持已存在的 Broadcast 默认，与未知值区分。
- TimeToLive 使用无损 duration 映射，不通过 uint 秒丢失精度或改变有效范围。ProviderKey/Metadata 继续 input-only，响应不回显凭据。
- 两种 transport 只绑定输入、调用同一控制 handler、转换结果；不复制 fan-out 或 subscription lifecycle。

验收：正常 Schemata 宿主实际端点可访问；等价 HTTP/gRPC/local 请求得到相同 owner、payload、target、options、outcome；非法 kind/空必需 target 字段无发送副作用；跨 owner 权限和 secret 边界保持。

#### R14 错误合同与动态值转换

- 所有 resx-keyed 异常调用使用有效资源 key 和结构化 args；替换 Flow/Push literal-message 调用；检查同类构造函数全量调用点。
- caller-owned 描述通过已有明确 description factory 或基础 `SchemataException` 合同表达；不能让完整句子成为 ErrorInfo.Reason，不再加模糊字符串 overload。
- InsightValidationException 在领域公共边界统一转换为 `SchemataException`，保留 reason/metadata；删除 Http/Grpc 各自的 Translate 表。Unimplemented、UnknownSourceName、InvalidArgument 在两种 transport 的规范含义一致。
- Insight 动态 gRPC 值映射明确覆盖公开模型允许的 primitive、enum、decimal、Guid、DateTime/Offset、TimeSpan、byte[]、map/list。ulong 超出 long 范围用无损十进制字符串；byte[] 用 base64；decimal 用无损字符串；既有有符号整数/浮点/bool/null 保持其类型槽。
- 禁止用任意对象 `ToString()` 兜底掩盖未支持类型；无法无损表示的类型在领域模型边界明确拒绝。
- gRPC 动态值的表示与 schema 一起定义；最终接收方还原的值与领域输出相同。

验收：有效 resx key 在 invariant/指定 culture 下产生预期消息与 reason；缺少 message 的 Push 请求不再返回默认 CLR exception 文案。Insight 同一错误两种 transport 保留相同 reason/metadata。`ulong.MaxValue`、byte[]、高精度 decimal、嵌套 map/list 的 wire round-trip 不丢值。

#### R15 Insight 有界执行、组合键与分页

- residual scan 的预算计算覆盖 backend 实际读取的行，不先丢弃不匹配行再计数；复用 `ResidualPage` 的 superset+predicate 边界。
- Order、Group、Estimated-total 的 materialization 统一使用现有有界 buffer 机制；每个缓冲源超过 `MaxResidualScanRows` 即失败，不先完整分配后检查。
- Group key 使用结构化、类型敏感的元组相等性；取消分隔符拼接与 culture-dependent ToString。null、含分隔符字符串和不同数据类型不能被错误合并。
- 分页 offset 统一验证非负和加法溢出；Insight 普通 Skip 与解码 token 使用同一边界；Report chunk/offset 保持非负验证。
- 复用 ASP.NET DataProtection 处理 continuation 的编码/完整性和参数绑定；在现有 Common/Resource/Insight/Report 位置抽取最小共享 codec，不建新的分页包。Resource 原有 token 保护规则保持；Insight/Report 绑定目标、规范请求及各自 continuation 数据；跨请求复用或篡改明确失败。

验收：零匹配/高选择性 residual、local Order/Group、Estimated 分支在达到预算后停止读取。分隔符/null/多列 Group 不误合并。负数 token、溢出、错误目标/查询、截断 token 被拒绝；正常分页无重复或漏项。

#### R16 共享 gRPC 描述符与 transport 生命周期

- Resource、Stream、Insight、Push 的 code-first service 都通过现有 `IGrpcServiceDescriptorContributor` 合并描述符；proto-first 继续走已有 BindServiceMethod 解析。
- Push/Insight 复用共享 gRPC transport 安装与 exception interceptor，删除局部复制 plumbing；每个注册方法的 marshaller、wire schema 和 reflection descriptor 使用同一模型来源。
- 保留 HTTP NDJSON 与 gRPC streaming 的不同终止表示，但都使用同一个 `IStreamDispatcher` 和 `IMessageExecutionScopeFactory`，不合并成 unary scope 或全量 buffer。

验收：reflection 能列出并调用 Resource、Stream、Insight、Push 的实际服务与方法；多个方法同 service 合并正确。pre-first failure、post-start failure、取消/提前停止都释放 scope 并保持协议区分。

#### R17 实体 query cache 与外部事务

- query cache transaction gate 只依赖真实 active UoW/enlistment 状态。
- 显式 Join 的查询在事务内绕过共享缓存，不读取事务外陈旧结果，不向共享缓存写入未提交结果。
- outer rollback 后查询结果不可留在公共 generation 中；outer commit 后按类型级失效恢复正常缓存。

验收：真实 provider 上用两个 enlisted repository/关联 projection 覆盖先写后查、相同实体类型不同 repository 实例、rollback、commit；以返回行与后续独立 scope 读值证明。

#### R18 Logout 会话证据归属

此单元保持 OAuth/OIDC 端点的 HTTP-only 协议边界；R01/R10 的 storage 生命周期是前置。

- `SessionParticipant` 只表示通知对象，永久记录不能证明当前或近期会话。
- EndSession 通过 `IOpSessionService` 及实际安装的 session evidence adapters 确定 canonical subject/session/RP 关联；不能直接用 ListParticipantsAsync 作为 active/recent 判断。
- 默认实现只承认现有可信 host/在线会话证据；应用 adapter 可以提供具有明确生命周期的近期证据。证据缺席走现有确认或失败路径，不新建固定 TTL、tombstone 或独立 recency 数据库。
- 有效 hint 的接受与是否需要用户确认分开。过期本身不无条件拒绝；当前/近期关联得到证明时按策略接受 hint。未关联当前 OP session/user 或没有 hint 时进入确认边界。
- malformed、bad-signature、foreign-issuer、wrong-client hint 不提供 target、通知、失效或 redirect 权威。合法 hint-only 不被强制转换成 cookie prerequisite；所需确认仍执行。
- inbound pairwise subject 在身份比较前变为 canonical；outbound 才投影各 RP subject。批准恢复检查 subject+session、单次消费，禁止旧确认作用于新 session。
- ownership 检查、session invalidation、host cookie clearing、通知和 participant retirement 指向同一目标。

主要来源：[RP-Initiated Logout 1.0 §2](https://openid.net/specs/openid-connect-rpinitiated-1_0.html#RPLogout) 对当前/近期 RP session 的过期 ID Token 接受是 SHOULD；无 hint 或不属于当前 OP session/user 时询问用户是 MUST；[§3](https://openid.net/specs/openid-connect-rpinitiated-1_0.html#RedirectionAfterLogout) 限制 post-logout redirect 的合法性。不把 SHOULD 改写为无条件静默注销。

验收：永久 participant+已失效会话、当前会话、应用提供的近期证据、cookie-less hint、不同 SID/用户、stale approval、无 interaction boundary、非法 hint、redirect/state，逐项观察零错误目标副作用或规范失败。通知列表不承担信任判断。

#### 删除与保留清单

必须删除或替换：

- `Schemata.Entity.Owner` 项目及其旧 namespace/reference。
- `Schemata.Caching.Distributed` 项目和框架 `AddDistributedCache` 适配入口。
- `IAtomicCacheProvider`、`ICacheCollectionProvider` 及对应 tenant 专用 wrapper。
- `CommitChanges<TEntity>`、entity 快照型 committed advisor 参数、repository 三份跟踪列表和 snapshot copies。
- `IRepository.GetWriteUnitOfWork`、Resource handler `_mutation` nullable fallback 与无消费者的 finalize 分支。
- Report `OwnSlot`、重复 conflict bit、具体 facade 类型强制消费要求。
- `DynamicRegistrationOptions.AllowUpdate/AllowDelete`、`SchemataQueryCacheOptions.EvictionEnabled`、Resource runtime authorization flag probe、全局 validation/freshness suppression bool。
- Actor IsService+manual new、重复可选服务 fallback、Flow 私有语言扫描目录。
- Insight transport-local Translate、无界 buffer、分隔符 Group key、未保护的手工 continuation codec。
- 被目标行为取代的旧行为测试、API/包文档和项目引用；不保留新旧并行路径以“兼容”测试。

保留的不同职责：

- `ICacheProvider` backend 语义与 tenant provider-container cache 的 lease/version 生命周期不同，后者不迁入字节缓存。
- `IResourceMutation` 的领域资源语义与 `IRepository/IUnitOfWork` 的 staging/事务语义不同。
- 事务 completed/dirty、lease retired/disposal、graph frozen、process version 不可重绑等状态保留。
- 单次 operation-scoped suppression 保留可恢复合同；不与全局能力开关混同。
- HTTP URI 与 gRPC body 的资源定位、204 与 Empty、软删除与硬删除响应差异按实际协议适配保留。
- 并发模型不同的 EF buffered save 与 LinqToDB immediate SQL 保留 provider 专属实现，但公共错误和原子性验收一致。
- 原始 repository 默认 Transient，不把每次解析的新实例误判成同 scope 的重复 enlistment。

## 4. 新能力合同

### U10 可靠执行基础能力（#186、#187、#188、#189、#190）

目标边界：

- 事件携带业务关联元数据，订阅侧可按关联过滤；Flow.Event 用同一关联机制完成信号匹配，不新建第二套关联存储（#186）。
- Flow 定时器支持网关分支武装、取消、重建与身份区分，与 #170 的修复合并验收（#170 + #187）。
- Scheduling 的 lease、重试与崩溃恢复语义定义为执行存储 provider 合同；默认 repository 实现以 provider 包提供，应用可替换后端；执行实体的用途限框架内部可靠调度与审计（#188）。
- Flow 外部副作用的执行意图与完成记录是引擎消费的 provider seam；意图携带稳定 request_id 转发给外部系统，恢复时经外部结果查询或以同一标识重发由接收端去重；外部协议两者都不支持时引擎上报结果未知而不承诺 exactly-once（#189）。
- Push 提供框架内置投递 provider 实现（首选 Web Push：RFC 8030 投递 + RFC 8291 消息加密 + RFC 8292 VAPID）；Push 核心不增加 outbox 式投递账本或重试状态机，发送失败的重试由应用通过 Scheduling 表达（#190）。

S03（共同提交语义）由 R01/R02/R10/R17 承载：Flow、Scheduling、业务数据在同一 UoW 中提交，提交后动作由 mutation owner 登记，不建立第二套 outbox 表。

上述能力复用 §1.1 第 2 条边界：框架提供内部可靠调度与执行记录，不提供面向应用业务消息的通用 inbox/outbox 存储产品。

### U11 出站传输与安全（#192、#194、#195）

- HTTP 消息签名：按 RFC 9421 实现为 transport 层通用能力，HTTP 与 gRPC（HTTP/2 消息层）共享；通过 attribute 或等价注册标记声明参与签名的派生组件（RFC 9421 §2.2）与字段（RFC 9421 §2.1），签名输出 Signature-Input/Signature（RFC 9421 §4.1/§4.2），算法取 RFC 9421 §6.2 注册表（初始算法见 RFC 9421 §3.3），密钥归既有 Security 所有权（#192）。
- Resource 分页与调用作用域：continuation 绑定 caller 身份与授权上下文，跨调用方不可复用（#194，与 R15 共用 protected continuation）。
- 请求幂等：遵循 AIP-155，可选 `request_id` 放在请求消息上而非资源字段，提供即必须保证幂等，重复请求返回先前成功响应（不可行时可返回当前资源状态），兑现窗口可配置；存储与并发复用现有 idempotency advisor 与原子缓存能力，不另设业务字段方案（#195，依赖 U6）。
- webhook 不内置：#191 已关闭，投递格式由接收方定义，框架用户以自定义 Push provider 实现。

## 5. 验证纪律与完成条件

### 5.1 测试与运行纪律

1. 每个 issue 的验收以其正文与本规格单元目标为准；实施时必须运行对应场景，禁止以属性存在、descriptor 数量或注释作为通过证据（#147）。
2. 按当前仓库测试平台运行。每批完成后构建一次，后续测试 `--no-build`；同一命令不重复执行；不为日志/TRX 重复执行相同命令；保留完整 stdout 和真实退出码；禁止通过管道只匹配 `error CS` 或最后一条命令退出码宣称全量通过。
3. 更新永久测试只针对消费者可观察行为、边界、事务、顺序和失败语义；不保留 descriptor 数量、源码文本、固定实现形状或 mock 回声测试。
4. 真实 EF/LinqToDB 使用仓内 SQLite fixtures；缓存 Memory 与 Redis 的证据分开，不能以 Memory 代替 Redis 跨进程 CAS。
5. 性能相关单元（R02 及 §2.4 低优先清理中的 #44 等）先记录 baseline，实现后同输入复测，记录原始值；无测量不写改善比例。
6. 跨进程/跨节点场景（Redis CAS、租户快照传播）需要真实隔离目标；目标缺席时在交付物中明确标记未执行，不从部分结果外推。

### 5.2 受影响套件

| 单元 | 首要现有套件/区域 | 必须观察的结果 |
|---|---|---|
| R01/R02/R03 | Entity.Repository.Tests/Integration、Entity.EntityFrameworkCore.Integration、Entity.LinqToDB.Integration、Identity.Tests/Integration、Authorization.Tests/Integration、Entity.Event.Tests | 单一 owner、外部事务不被子操作提交/回滚、重复 EF staging 正确、事件只在 mutation outer commit 后触发、无 entity snapshots |
| R04/R17、U6 | Caching.Memory.Integration、Caching.Redis.Tests、Entity.Cache.Tests、Tenancy.Caching.Integration | 全能力同后端、两进程 CAS、tenant scalar/enumerable 隔离、事务中缓存绕过 |
| R05/R06/R07 | Core.Tests、Modular.Tests、Resource.Tests/Http/Grpc.Integration、Messaging/RabbitMq、Event/Flow.Event/Actor.Event suites、Tenancy.Tests/Integration | feature 顺序与缺席、tenant alias、typed+catch-all 协作、不可变 metadata |
| R08 | Report.Tests/Integration/Actor | 标准接口+custom inner 的 guard、周期新建/修改/删除、同名 snapshot 跨父级读取隔离 |
| R09/R11 | Flow.Tests/Bpmn.Tests/Integration、Resource purge、Identity claim deletion suites | 真实表达式结果、冻结、外层 rollback、活动 reader 不被 context 切换破坏 |
| R10 | Authorization.Tests/Integration、Security.Tests、Entity provider integration | replay 双撤销及 CAS loser 在真实存储中完成，协议错误而非 completed/disposed 异常 |
| R12 | Tenancy.Tests/Integration | disposal failure 全成员清理、无丢失 lease、在用 provider 不提前销毁 |
| R13/R14/R16 | Push.Http/Grpc.Integration、Insight.Http/Grpc.Integration、Transport.Grpc.Tests、Resource transport suites | 正常 Schemata host 安装、wire 等价、错误 metadata 和 reflection |
| R15 | Insight.Tests、Insight transport suites、Report snapshot paging、Resource paging suites | 扫描预算在原始数据边界生效、Group 无碰撞、分页 token 校验一致 |
| R18 | Authorization.Tests、Authorization.Integration.Tests 的 EndSession/LogoutInteraction/session/notifier 区域 | 永久 participant 不提供会话权威；当前/近期证据、hint-only、确认恢复、redirect/state 均指向同一合法目标 |
| U10 | Event.Foundation.Tests、Event.Integration.Tests、Flow.Tests/Integration、Scheduling.Tests/Integration、Push.Tests | 关联过滤与信号匹配一次投递；timer 取消/重建/身份拒绝；lease 过期回收与重试上限；意图记录随 transition 回滚消失；Web Push 实际投递与签名可验 |
| U11 | Security.Tests、Transport.Grpc.Tests、Resource.Tests/Http/Grpc.Integration | RFC 9421 签名在 HTTP 与 gRPC 同面定义可验；continuation 跨 principal/租户/参数失败；request_id 重复返回先前响应且只有一次 mutation |
| §2.1 缺陷聚类 | Identity.Tests、Authorization.Tests/Integration、Resource.Tests、Insight.Tests、Actor.Foundation.Tests、Flow.Tests/Integration、Scheduling.Tests/Integration、Push.Tests、Messaging.RabbitMq.Tests、Report.Tests | 各 issue 正文验收与所属 §3 合同的可观察结果 |

新建场景加入最近的现有 suite，不为每个缺陷新增测试项目。最终集成命令使用仓库 SDK `10.0.201`：先 `dotnet build Schemata.slnx -c Release`，成功后 `dotnet test Schemata.slnx -c Release --no-build`。browser/Redis/跨平台未执行场景另列。

### 5.3 完成条件

1. U1–U11 与 R01–R18 的行为验收逐项有实际输出，未执行环境单列。
2. 所有资源语义入口进入同一 mutation owner；旧 committed entity 快照与无条件 entity list/dict 已删除；provider tracker 与 UoW 状态各归其 owner。
3. 能力安装、服务解析、HTTP/gRPC/local 行为同源；没有功能状态位、descriptor 清理或 manual new 维持第二条能力路径。
4. 每个被删除接口/项目完成仓内 caller、test、docs、solution、targets 和 package 依赖切换，不保留空项目或 shim。
5. 未实现能力仍明确不在范围，extension point 不作为完整功能或协议认证的证据。
6. 后续提交与 issue 关闭只引用该单元实际行为验证；历史测试结果不能替代本规格改变路径后的验收。

## 6. 剩余工作的实施顺序

1. **缺陷修复（§2.1）**按组件聚类分派：Authorization（#153、#154、#155、#161、#162、#180、#146）；Resource/Insight（#156、#157、#179、#181）；Identity（#158）；Scheduling（#163、#167、#168、#169）；Flow（#170–#178）；Push（#166）；Entity/Transport/Messaging（#159、#160、#165）；Report（#182）；Actor（#164）；横向项（#145、#183、#185、#147）随触及的单元一并完成。
2. **新能力（§4）**在缺陷修复验证后启动：U10（#186、#187、#188、#189、#190）与 U11（#192、#194、#195）。#194 使用 R15 的 protected continuation codec；#195 使用 U6 的原子缓存能力。
3. **独立排期**：#41 主动密钥轮换（§2.4 延期项，启动时单独授权）；#74 双 origin 浏览器验收场景（仅需浏览器验证环境）。
4. **集成验证**：全部剩余工作完成后按 §5.3 逐项核对完成条件；每个 issue 的关闭引用其实际行为验证。

共享文件由一名集成 owner 维护；独立 provider/transport 实现在合同冻结后分派，禁止多个分支各发明接口。push/merge 另行授权。

执行状态（2026-10-04 暂停，随执行更新）：缺陷聚类中 Authorization、Resource/Insight、Identity、Scheduling 四组已完成并逐任务评审通过，落在分支 `sdd/framework-remediation-2026-10-03`（HEAD `346271d7`，未推送，issue 尚未关闭）；其余聚类与 U10/U11 未启动。任务级进度、BASE/HEAD 与恢复步骤见 `docs/superpowers/plans/2026-10-03-framework-remediation.md` 末节与 `.superpowers/sdd/framework-remediation-2026-10-03/progress.md`。
