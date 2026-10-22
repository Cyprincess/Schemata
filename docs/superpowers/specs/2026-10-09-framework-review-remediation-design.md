# 框架审查修复与非侵入式多租户设计

## 目标与授权

修复 `00c34ae07` 至 `4a30881b293d0e67fa68d2aca4fdab4593d99ee4` 审查确认的问题，包括正确性、一致性、维护性、测试与文档。完整问题集为 #206–#285，另包含已有 issues 的审查补充与租户侵入式实现清理。审查候选被驳回或仍缺契约证据的项不机械实施。

用户已批准修复全部问题；以下新增架构设计仍需书面审阅。文档位于既有未跟踪 `docs/superpowers/` 下，不提交该目录，不覆盖用户文件。

## 已确定的策略

- Authorization `RelaxXXX` 是用户支持的降级选择，保留 API、配置以及显式降级行为；修复默认/绑定/并发/错误路径，文档说明实际标准权衡。#224 已撤回删除选项的方向。
- 保留 canonical-name OIDC `sub`。不新增 Oid、UID 协议投影、UID FK、历史 token 兼容读取或迁移工具。
- `[ResourceReference]` 标记 string 完整 canonical-name 关联；单目标和根据 canonical name 自动解析的多目标保持原契约。结构父级的 leaf 字段与完整 ref 字段分开。
- `CanonicalName` 是资源身份，对外映射为 `name`；`display_name` 不是身份。Name 生成由消费者 repository add/create advisor 提供，框架不填 Guid/random fallback。
- 用户当前不支持自定义 resource(canonical) name；不从缺少某个 unique-index 注解推出支持重复名称。AIP-122 要求所属 API 内唯一；AIP-164 说明保留的软删除 ID 不复用。永久删除后的永久保留没有从这些条款得到要求。OIDC 永不重新分配是另一要求；不通过本次修复新增墓碑或永久名称注册表。
- 不保留废弃 alias、双实现或双写，不新增依赖掩盖问题，不增加 startup 环境 guard、遥测、outbox、leader election。

## 非侵入式 Tenancy 的边界

租户逻辑只存在于 `Schemata.Tenancy.*`。其他组件不读取 TenantContext、不设置/比较 TenantUid、不拼租户缓存键、不解析租户消息头、不选择 tenant scope。Abstractions 可声明被动数据契约；用户已明确允许一个统一被动 trait/列。

### Repository 隔离

- 在 `Schemata.Abstractions.Entities` 声明单一被动 `ITenant`：`Guid? TenantUid { get; set; }`。null 表示 host 行，与没有安装 Tenancy 的普通运行保持可表达。
- 统一 Flow、Actor、Scheduling 当前 string Tenant / Guid? TenantUid 两条数据路径。相关子记录必须遵循同一租户实体隔离机制，不能假定只凭 parent canonical string 就安全。
- 被动 trait 不携带行为。实体使用普通 CLR scalar 映射；不引入 ORM shadow/dynamic-column 两种新存储实现。
- Tenancy 启用时，通过 `TryAddEnumerable` 注册 repository add/build-query/update/remove advisors。Add 在 canonical-name/uniqueness前写入当前身份；query 对 concrete entity 生成可翻译谓词；update/remove 以当前身份、原持久化身份和实际 repository mutation 结果校验跨租户与重新归属。
- 非 host 跨租户写入明确失败，不用 Block→NoWrite 把拒绝变成成功。QueryOwnerSuppressed 不得成为 tenant isolation bypass。Host默认只能看host数据，不能因 null 自动看全部租户。
- Host tenant catalog 是共享元数据，不把 tenant自身分区成自引用。框架关联、规范父级和业务字段仍 canonical-name ref。
- 默认租户行层机制由 Tenancy Foundation 持有；优先复用现有 Entity.Repository 依赖，不为一个 advisor 集合创建新包。
- 注册隔离的实体包括当前带租户数据的 Flow process/token/transition/participant、Actor state、Scheduling job/execution，并覆盖其直接持久化子记录；业务消费者通过同一被动trait加入。不能把存在parent ref等同于子记录自动隔离。
- tenant字段只作为内部持久化数据，不通过业务request/detail/wire公开；使用已有通用serializer忽略元数据，不在通用wire规则中增加tenant special case。Host metadata与tenant data的注册集合必须明确，不能靠属性名反射猜测是否隔离。
- 不复用owner suppression绕过tenant advisor；后台跨租户读取只能由Tenancy-owned适配器获得明确授权的host数据访问入口，然后进入目标scope执行。数据源选择仍可由消费者tenant DI提供，不强制数据库-per-tenant，也不提供迁移工具。

### Cache 与 protected continuation

- 保留 Tenancy.Caching 的一个 `ICacheProvider` decorator，对 Backend/Selected 出口应用统一 framing。Entity.Cache 与消费者不执行租户分支；所有原子 primitive保持一个 backend原子操作。
- continuation 必须保留跨租户拒绝，不能删字段后依赖 repository filter声称安全。
- Tenancy 使用现有 `IDataProtectionProvider` / `IDataProtector.CreateProtector` seam 包装 purpose，运行时按tenant选择派生protector；Resource/Insight/Report删除Tenant字段与比较，不感知Tenancy。
- 保护器不得在 singleton首次构造时冻结 tenant。Generic protection purpose保持原调用；Tenancy-owned protector在 Protect/Unprotect时取当前绑定，调用者不用改成租户API。
- Cookie/antiforgery/login continuation影响必须实际验证；不能假设 host/tenant middleware先后顺序自动一致。优先在Tenancy Foundation复用当前数据保护能力，不新增 Protection包。
- 不引入第三种continuation scope bag；保留Caller与query绑定，tenant切换由purpose/key隔离拒绝。Cookie共享host票据与租户票据的区别在Tenancy-owned保护策略/注册中明确，不能通过new Realm/extra token tenant字段掩盖。

### 消息与执行 scope

- `IMessageContextPropagator` 已是 enumerable capture/restore seam，复用它；`MessageContext.Items` 是扁平、序列化的通用传播数据，不加入Tenant typed字段。
- Messaging.Skeleton 删除 TenantIdentity、TenantContext、tenancy key和host-only guard。Tenancy.Messaging实现 propagator、解析/校验 key与scope选择。
- 当前 MessageExecutionScope 保持一个 concrete scope；移除 TenantIdentity，用 factory提供的同步 enter lease委托表达任意执行上下文进入。CreateAsync只创建scope，调用者在 await后同步Enter，finally释放；不新建平行泛型/non-generic scope接口。
- Stream用同一MessageContext、factory和propagator，在每次MoveNext/Dispose恢复同一捕获执行上下文。Tenant身份不再进入stream context。
- Scheduling持久化通用MessageContext快照并恢复；它不编码Tenant字段、不解释propagation keys。Job/execution creation在tenant-bound repository scope执行。
- Host后台跨租户枚举与随后scope选择由Tenancy-owned执行适配器承担，采用当前execution-store与factory边界，不能把ITenantServiceScopeFactory调用搬进Scheduling。
- 通用持久化执行上下文只保存现有propagator输出，在job创建与execution生成时通过同一捕获owner复制，后台恢复经同一factory。Component仅传递通用context，不解析`tenancy.*` key。
- 通用enter lease每次Enter产生自己的可释放lease；scope只拥有DI资源，不能在async Create中设置ambient后指望跨await传回调用者，也不能在scope Dispose重复释放调用者已经拥有的lease。

### Actor

- ActorId仅保留Type/Key，不携带TenantIdentity；Actor runtime删除tenant equality检查与tenant消息Bind。
- Tenancy-owned IActorSystem decorator按tenant选择独立的现有InProcessActorSystem实例；ActorId与actor业务key不被编码租户前缀。Decorator拥有runtime注册、idle收集与shutdown，actor-ref保存生成时的通用MessageContext并在send/stop/timer回调恢复，阻止把A的引用当B的当前引用使用。
- 长期actor constructor仍host/activation lifetime依赖；每turn服务仍从IActorContext.Services解析。不能为本次清理重新定义构造器tenant scope。
- Ask envelope显式保留声明的TRequest/TResponse，keyed dispatch不依赖payload.GetType或first IRequest接口。
- Actor runtime的通用生命周期补齐IAsyncDisposable资源释放入口，Tenancy decorator与host ActorHostedService复用同一生命周期，不重新实现mailbox。若依赖方向需要Tenancy.Actor桥接包，它只承载实际actor适配与启用，不能并入Actor或把Actor依赖拉入所有Tenancy Foundation消费者；该包必要性由精确依赖图在实施计划中记录。

## 其余问题的 owner分组

|分组|完整issue集合|
|---|---|
|Kernel/Transport|207,212,231,233,234,236,237,250,262,265,279|
|Expressions/Data/Mapping|206,208,209,221,226,238,252,253,254,255,256,257,258,259,261,267,272,274|
|Authorization/Identity|214,223,224,225,228,229,239,240,241,242,243,244,245,246,247,260,268,275,276,280|
|Resource/Insight/Report/Push|210,211,213,216,222,227,235,248,273,278|
|Flow/Actor/Event|215,218,219,220,230,232,249,251,269,270,271,281,282,283,284,285|
|Scheduling/Tenancy|217,263,264,266,277|

每个issue原始证据、当前修正、受影响文件与确认/限定项见 `local://review-20261009-final-delivery.json`、`local://review-20261009-integrated-dispositions.json`。执行前逐owner读取当前源文件，原审查不是自动codemod指令。

## 统一实现约束

- Resource使用已有MutationResult保持Applied/NoWrite差异，合法Handle/no-op不机械改为异常；不得把Block或未写入实体映射成201/changed成功。
- Flow统一activation lineage、scope completion、graph admission、payload、loop、compensation owner；现有Spawner/transition身份优先于新增状态位。
- Scheduling租约覆盖setup与body，inline LRO复用当前租约/取消owner，明确expired ownership recovery；不用最近更新时间猜测alive，不承诺exactly-once。
- AIP/CEL保持语言-specific语义，准确数值/地图/equality/hash与AST cache identity一处拥有；不添加第二compiler/evaluator。
- Security signature保持字节、quoted字段、curve与age比较语义，错误直接传播；不增加泛化fallback。
- Constants归并到对应component Constants，code/test/doc的所有caller同步迁移。
- 文档以实际shipped路径改写。测试删除弱marker/default/copy行，不重钉实现细节；新增测试只防真实输入、并发、状态或错误边界。

## 验收与验证

1. 完整80个issue及existing owner补充有逐项处置，不丢掉一致性/维护性/文档测试项。#224/#228按用户修正，不照原错误方向实现。
2. 无Tenancy enabled时无需租户服务，行为与普通framework保持一致。
3. 两租户同业务key并行执行、cache/idempotency/continuation/broker/actor/后台恢复均隔离；Host catalog可用但默认host scope不读取tenant行。
4. 同一数据隔离advisor机制覆盖所有租户实体及provider；update/remove与直接repository路径都不能跨租户修改。
5. build统一由integration owner执行一次，然后各test command一次`--no-build`，仅实际暴露需修复时重新构建相关产物；子代理实现中不自行运行build/test/lint/formatter。
6. 运行真实HTTP/CLI/throwaway consumer smoke覆盖改变的边界；不能仅以mock测试/编译报告通过。Broker外部前提不满足时不造输出，完成可达真实消费路径并说明未运行的surface。
7. 完成后清除throwaway scaffolds，更新shipped docs；不编辑generated changelog段，不改eng/common upstream，不提交user-owned未跟踪内容。

## 待书面审阅的架构点

被动ITenant数据契约已获用户选择。需确认：Tenancy-owned scope/protection/actor/execution适配方式是否符合无侵入要求，以及host跨租户后台枚举权限边界。确认后形成精确实施计划与租赁slice，再进入代码。
