# Source project reference graph (HEAD 90ff2a5f)

Derived from `ProjectReference` in `src/*/*.csproj`. `Area` is the second dotted segment.

| Project | Area | References (same-area marked *) |
|---|---|---|
| Schemata.Abstractions | Abstractions | — |
| Schemata.Actor.Event | Actor | **Schemata.Actor.Foundation**, Schemata.Core, Schemata.Event.Foundation |
| Schemata.Actor.Foundation | Actor | **Schemata.Actor.Skeleton**, Schemata.Abstractions, Schemata.Core, Schemata.Entity.Repository, Schemata.Messaging.Skeleton |
| Schemata.Actor.Scheduling | Actor | **Schemata.Actor.Foundation**, Schemata.Core, Schemata.Scheduling.Foundation |
| Schemata.Actor.Skeleton | Actor | Schemata.Abstractions, Schemata.Messaging.Skeleton |
| Schemata.Advice | Advice | Schemata.Abstractions |
| Schemata.Authorization.Foundation | Authorization | **Schemata.Authorization.Skeleton**, Schemata.Caching.Skeleton, Schemata.Common, Schemata.Core, Schemata.Messaging.Skeleton, Schemata.Scheduling.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton, Schemata.Transport.Http |
| Schemata.Authorization.Grpc | Authorization | **Schemata.Authorization.Foundation**, Schemata.Resource.Grpc |
| Schemata.Authorization.Http | Authorization | **Schemata.Authorization.Foundation**, Schemata.Resource.Http |
| Schemata.Authorization.Identity | Authorization | **Schemata.Authorization.Foundation**, Schemata.Core, Schemata.Identity.Foundation |
| Schemata.Authorization.Skeleton | Authorization | Schemata.Entity.Repository, Schemata.Security.Skeleton |
| Schemata.Caching.Memory | Caching | **Schemata.Caching.Skeleton** |
| Schemata.Caching.Redis | Caching | **Schemata.Caching.Skeleton** |
| Schemata.Caching.Skeleton | Caching | — |
| Schemata.Common | Common | Schemata.Abstractions |
| Schemata.Core | Core | Schemata.Abstractions, Schemata.Common, Schemata.Expressions.Skeleton, Schemata.Security.Skeleton |
| Schemata.Domain.Skeleton | Domain | Schemata.Abstractions, Schemata.Event.Skeleton |
| Schemata.Entity.Cache | Entity | **Schemata.Entity.Repository**, Schemata.Caching.Skeleton, Schemata.Common |
| Schemata.Entity.EntityFrameworkCore | Entity | **Schemata.Entity.Repository** |
| Schemata.Entity.Event | Entity | **Schemata.Entity.Repository**, Schemata.Event.Skeleton |
| Schemata.Entity.LinqToDB | Entity | **Schemata.Entity.Repository** |
| Schemata.Entity.Repository | Entity | Schemata.Advice, Schemata.Common, Schemata.Validation.Skeleton |
| Schemata.Event.Foundation | Event | **Schemata.Event.Skeleton**, Schemata.Core, Schemata.Entity.Repository, Schemata.Messaging.Skeleton |
| Schemata.Event.RabbitMq | Event | **Schemata.Event.Foundation**, **Schemata.Event.Skeleton**, Schemata.Transport.RabbitMq |
| Schemata.Event.Skeleton | Event | Schemata.Abstractions, Schemata.Advice, Schemata.Messaging.Skeleton |
| Schemata.Expressions.Aip | Expressions | **Schemata.Expressions.Skeleton**, Schemata.Common |
| Schemata.Expressions.Cel | Expressions | **Schemata.Expressions.Skeleton**, Schemata.Common |
| Schemata.Expressions.Order | Expressions | **Schemata.Expressions.Skeleton**, Schemata.Common |
| Schemata.Expressions.Skeleton | Expressions | — |
| Schemata.Flow.Actor | Flow | **Schemata.Flow.Foundation**, Schemata.Actor.Foundation |
| Schemata.Flow.Bpmn | Flow | **Schemata.Flow.Foundation** |
| Schemata.Flow.Event | Flow | **Schemata.Flow.Foundation**, **Schemata.Flow.Skeleton**, Schemata.Event.Foundation, Schemata.Event.Skeleton |
| Schemata.Flow.Foundation | Flow | **Schemata.Flow.Repository**, **Schemata.Flow.Skeleton**, Schemata.Core, Schemata.Entity.Repository, Schemata.Event.Skeleton, Schemata.Expressions.Skeleton, Schemata.Messaging.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton |
| Schemata.Flow.Grpc | Flow | **Schemata.Flow.Foundation**, Schemata.Resource.Grpc |
| Schemata.Flow.Http | Flow | **Schemata.Flow.Foundation**, Schemata.Resource.Http |
| Schemata.Flow.Repository | Flow | **Schemata.Flow.Skeleton**, Schemata.Entity.Repository |
| Schemata.Flow.Scheduling | Flow | **Schemata.Flow.Foundation**, Schemata.Scheduling.Foundation, Schemata.Scheduling.Skeleton |
| Schemata.Flow.Skeleton | Flow | Schemata.Common, Schemata.Entity.Repository, Schemata.Expressions.Skeleton, Schemata.Messaging.Skeleton |
| Schemata.Flow.StateMachine | Flow | **Schemata.Flow.Foundation**, **Schemata.Flow.Skeleton** |
| Schemata.Identity.Foundation | Identity | **Schemata.Identity.Skeleton**, Schemata.Core, Schemata.Messaging.Skeleton, Schemata.Security.Foundation, Schemata.Transport.Http |
| Schemata.Identity.Grpc | Identity | **Schemata.Identity.Foundation**, Schemata.Resource.Grpc |
| Schemata.Identity.Http | Identity | **Schemata.Identity.Foundation**, Schemata.Resource.Http |
| Schemata.Identity.Scim | Identity | **Schemata.Identity.Foundation**, Schemata.Scim |
| Schemata.Identity.Skeleton | Identity | Schemata.Entity.Repository |
| Schemata.Insight.Foundation | Insight | **Schemata.Insight.Skeleton**, Schemata.Common, Schemata.Core, Schemata.Entity.Repository, Schemata.Expressions.Skeleton, Schemata.Messaging.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton |
| Schemata.Insight.Grpc | Insight | **Schemata.Insight.Foundation**, Schemata.Transport.Grpc |
| Schemata.Insight.Http | Insight | **Schemata.Insight.Foundation**, Schemata.Transport.Http |
| Schemata.Insight.Skeleton | Insight | Schemata.Advice, Schemata.Common, Schemata.Expressions.Skeleton, Schemata.Messaging.Skeleton |
| Schemata.Mapping.AutoMapper | Mapping | **Schemata.Mapping.Foundation** |
| Schemata.Mapping.Foundation | Mapping | **Schemata.Mapping.Skeleton**, Schemata.Core |
| Schemata.Mapping.Mapster | Mapping | **Schemata.Mapping.Foundation** |
| Schemata.Mapping.Skeleton | Mapping | Schemata.Abstractions, Schemata.Common |
| Schemata.Messaging.RabbitMq | Messaging | **Schemata.Messaging.Skeleton**, Schemata.Transport.RabbitMq |
| Schemata.Messaging.Skeleton | Messaging | Schemata.Abstractions |
| Schemata.Modular | Modular | Schemata.Core |
| Schemata.Push.Actor | Push | **Schemata.Push.Foundation**, Schemata.Actor.Foundation |
| Schemata.Push.Foundation | Push | **Schemata.Push.Skeleton**, Schemata.Core, Schemata.Messaging.Skeleton |
| Schemata.Push.Grpc | Push | **Schemata.Push.Foundation**, Schemata.Transport.Grpc |
| Schemata.Push.Http | Push | **Schemata.Push.Foundation**, Schemata.Transport.Http |
| Schemata.Push.Scheduling | Push | **Schemata.Push.Foundation**, Schemata.Scheduling.Foundation |
| Schemata.Push.Skeleton | Push | Schemata.Abstractions, Schemata.Advice, Schemata.Messaging.Skeleton |
| Schemata.Push.WebPush | Push | **Schemata.Push.Foundation** |
| Schemata.Report.Actor | Report | **Schemata.Report.Foundation**, Schemata.Actor.Foundation |
| Schemata.Report.Foundation | Report | **Schemata.Report.Skeleton**, Schemata.Advice, Schemata.Insight.Foundation, Schemata.Messaging.Skeleton, Schemata.Resource.Foundation, Schemata.Scheduling.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton |
| Schemata.Report.Grpc | Report | **Schemata.Report.Foundation**, Schemata.Resource.Grpc |
| Schemata.Report.Http | Report | **Schemata.Report.Foundation**, Schemata.Resource.Http |
| Schemata.Report.Scheduling | Report | **Schemata.Report.Foundation**, Schemata.Scheduling.Foundation |
| Schemata.Report.Skeleton | Report | Schemata.Abstractions, Schemata.Common, Schemata.Insight.Skeleton |
| Schemata.Resource.Foundation | Resource | Schemata.Caching.Skeleton, Schemata.Core, Schemata.Entity.Repository, Schemata.Expressions.Skeleton, Schemata.Mapping.Skeleton, Schemata.Messaging.Skeleton, Schemata.Scheduling.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton |
| Schemata.Resource.Grpc | Resource | **Schemata.Resource.Foundation**, Schemata.Transport.Grpc |
| Schemata.Resource.Http | Resource | **Schemata.Resource.Foundation**, Schemata.Transport.Http |
| Schemata.Scheduling.Event | Scheduling | **Schemata.Scheduling.Foundation**, **Schemata.Scheduling.Skeleton**, Schemata.Event.Foundation, Schemata.Event.Skeleton |
| Schemata.Scheduling.Foundation | Scheduling | **Schemata.Scheduling.Skeleton**, Schemata.Core, Schemata.Entity.Repository, Schemata.Mapping.Skeleton, Schemata.Messaging.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton |
| Schemata.Scheduling.Grpc | Scheduling | **Schemata.Scheduling.Foundation**, Schemata.Resource.Grpc |
| Schemata.Scheduling.Http | Scheduling | **Schemata.Scheduling.Foundation**, Schemata.Resource.Http |
| Schemata.Scheduling.Repository | Scheduling | **Schemata.Scheduling.Skeleton**, Schemata.Entity.Repository |
| Schemata.Scheduling.Skeleton | Scheduling | Schemata.Abstractions, Schemata.Advice, Schemata.Messaging.Skeleton |
| Schemata.Scim | Scim | Schemata.Abstractions, Schemata.Common, Schemata.Transport.Http |
| Schemata.Security.Foundation | Security | **Schemata.Security.Skeleton**, Schemata.Caching.Skeleton, Schemata.Core, Schemata.Entity.Repository, Schemata.Messaging.Skeleton |
| Schemata.Security.Skeleton | Security | Schemata.Common, Schemata.Entity.Repository, Schemata.Messaging.Skeleton |
| Schemata.Tenancy.Actor | Tenancy | **Schemata.Tenancy.Messaging**, Schemata.Actor.Foundation |
| Schemata.Tenancy.Caching | Tenancy | **Schemata.Tenancy.Foundation**, Schemata.Caching.Skeleton |
| Schemata.Tenancy.Foundation | Tenancy | **Schemata.Tenancy.Skeleton**, Schemata.Common, Schemata.Core, Schemata.Entity.Repository |
| Schemata.Tenancy.Messaging | Tenancy | **Schemata.Tenancy.Foundation**, Schemata.Messaging.Skeleton |
| Schemata.Tenancy.Skeleton | Tenancy | Schemata.Abstractions |
| Schemata.Transport.Grpc | Transport | Schemata.Common, Schemata.Core, Schemata.Messaging.Skeleton, Schemata.Security.Foundation |
| Schemata.Transport.Http | Transport | Schemata.Common, Schemata.Core, Schemata.Messaging.Skeleton |
| Schemata.Transport.RabbitMq | Transport | — |
| Schemata.Validation.FluentValidation | Validation | **Schemata.Validation.Skeleton**, Schemata.Abstractions, Schemata.Advice, Schemata.Messaging.Skeleton, Schemata.Security.Skeleton |
| Schemata.Validation.Skeleton | Validation | Schemata.Abstractions, Schemata.Advice |

## Cross-area edges only

- Schemata.Actor.Event -> Schemata.Core, Schemata.Event.Foundation
- Schemata.Actor.Foundation -> Schemata.Abstractions, Schemata.Core, Schemata.Entity.Repository, Schemata.Messaging.Skeleton
- Schemata.Actor.Scheduling -> Schemata.Core, Schemata.Scheduling.Foundation
- Schemata.Actor.Skeleton -> Schemata.Abstractions, Schemata.Messaging.Skeleton
- Schemata.Advice -> Schemata.Abstractions
- Schemata.Authorization.Foundation -> Schemata.Caching.Skeleton, Schemata.Common, Schemata.Core, Schemata.Messaging.Skeleton, Schemata.Scheduling.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton, Schemata.Transport.Http
- Schemata.Authorization.Grpc -> Schemata.Resource.Grpc
- Schemata.Authorization.Http -> Schemata.Resource.Http
- Schemata.Authorization.Identity -> Schemata.Core, Schemata.Identity.Foundation
- Schemata.Authorization.Skeleton -> Schemata.Entity.Repository, Schemata.Security.Skeleton
- Schemata.Common -> Schemata.Abstractions
- Schemata.Core -> Schemata.Abstractions, Schemata.Common, Schemata.Expressions.Skeleton, Schemata.Security.Skeleton
- Schemata.Domain.Skeleton -> Schemata.Abstractions, Schemata.Event.Skeleton
- Schemata.Entity.Cache -> Schemata.Caching.Skeleton, Schemata.Common
- Schemata.Entity.Event -> Schemata.Event.Skeleton
- Schemata.Entity.Repository -> Schemata.Advice, Schemata.Common, Schemata.Validation.Skeleton
- Schemata.Event.Foundation -> Schemata.Core, Schemata.Entity.Repository, Schemata.Messaging.Skeleton
- Schemata.Event.RabbitMq -> Schemata.Transport.RabbitMq
- Schemata.Event.Skeleton -> Schemata.Abstractions, Schemata.Advice, Schemata.Messaging.Skeleton
- Schemata.Expressions.Aip -> Schemata.Common
- Schemata.Expressions.Cel -> Schemata.Common
- Schemata.Expressions.Order -> Schemata.Common
- Schemata.Flow.Actor -> Schemata.Actor.Foundation
- Schemata.Flow.Event -> Schemata.Event.Foundation, Schemata.Event.Skeleton
- Schemata.Flow.Foundation -> Schemata.Core, Schemata.Entity.Repository, Schemata.Event.Skeleton, Schemata.Expressions.Skeleton, Schemata.Messaging.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton
- Schemata.Flow.Grpc -> Schemata.Resource.Grpc
- Schemata.Flow.Http -> Schemata.Resource.Http
- Schemata.Flow.Repository -> Schemata.Entity.Repository
- Schemata.Flow.Scheduling -> Schemata.Scheduling.Foundation, Schemata.Scheduling.Skeleton
- Schemata.Flow.Skeleton -> Schemata.Common, Schemata.Entity.Repository, Schemata.Expressions.Skeleton, Schemata.Messaging.Skeleton
- Schemata.Identity.Foundation -> Schemata.Core, Schemata.Messaging.Skeleton, Schemata.Security.Foundation, Schemata.Transport.Http
- Schemata.Identity.Grpc -> Schemata.Resource.Grpc
- Schemata.Identity.Http -> Schemata.Resource.Http
- Schemata.Identity.Scim -> Schemata.Scim
- Schemata.Identity.Skeleton -> Schemata.Entity.Repository
- Schemata.Insight.Foundation -> Schemata.Common, Schemata.Core, Schemata.Entity.Repository, Schemata.Expressions.Skeleton, Schemata.Messaging.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton
- Schemata.Insight.Grpc -> Schemata.Transport.Grpc
- Schemata.Insight.Http -> Schemata.Transport.Http
- Schemata.Insight.Skeleton -> Schemata.Advice, Schemata.Common, Schemata.Expressions.Skeleton, Schemata.Messaging.Skeleton
- Schemata.Mapping.Foundation -> Schemata.Core
- Schemata.Mapping.Skeleton -> Schemata.Abstractions, Schemata.Common
- Schemata.Messaging.RabbitMq -> Schemata.Transport.RabbitMq
- Schemata.Messaging.Skeleton -> Schemata.Abstractions
- Schemata.Modular -> Schemata.Core
- Schemata.Push.Actor -> Schemata.Actor.Foundation
- Schemata.Push.Foundation -> Schemata.Core, Schemata.Messaging.Skeleton
- Schemata.Push.Grpc -> Schemata.Transport.Grpc
- Schemata.Push.Http -> Schemata.Transport.Http
- Schemata.Push.Scheduling -> Schemata.Scheduling.Foundation
- Schemata.Push.Skeleton -> Schemata.Abstractions, Schemata.Advice, Schemata.Messaging.Skeleton
- Schemata.Report.Actor -> Schemata.Actor.Foundation
- Schemata.Report.Foundation -> Schemata.Advice, Schemata.Insight.Foundation, Schemata.Messaging.Skeleton, Schemata.Resource.Foundation, Schemata.Scheduling.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton
- Schemata.Report.Grpc -> Schemata.Resource.Grpc
- Schemata.Report.Http -> Schemata.Resource.Http
- Schemata.Report.Scheduling -> Schemata.Scheduling.Foundation
- Schemata.Report.Skeleton -> Schemata.Abstractions, Schemata.Common, Schemata.Insight.Skeleton
- Schemata.Resource.Foundation -> Schemata.Caching.Skeleton, Schemata.Core, Schemata.Entity.Repository, Schemata.Expressions.Skeleton, Schemata.Mapping.Skeleton, Schemata.Messaging.Skeleton, Schemata.Scheduling.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton
- Schemata.Resource.Grpc -> Schemata.Transport.Grpc
- Schemata.Resource.Http -> Schemata.Transport.Http
- Schemata.Scheduling.Event -> Schemata.Event.Foundation, Schemata.Event.Skeleton
- Schemata.Scheduling.Foundation -> Schemata.Core, Schemata.Entity.Repository, Schemata.Mapping.Skeleton, Schemata.Messaging.Skeleton, Schemata.Security.Foundation, Schemata.Security.Skeleton
- Schemata.Scheduling.Grpc -> Schemata.Resource.Grpc
- Schemata.Scheduling.Http -> Schemata.Resource.Http
- Schemata.Scheduling.Repository -> Schemata.Entity.Repository
- Schemata.Scheduling.Skeleton -> Schemata.Abstractions, Schemata.Advice, Schemata.Messaging.Skeleton
- Schemata.Scim -> Schemata.Abstractions, Schemata.Common, Schemata.Transport.Http
- Schemata.Security.Foundation -> Schemata.Caching.Skeleton, Schemata.Core, Schemata.Entity.Repository, Schemata.Messaging.Skeleton
- Schemata.Security.Skeleton -> Schemata.Common, Schemata.Entity.Repository, Schemata.Messaging.Skeleton
- Schemata.Tenancy.Actor -> Schemata.Actor.Foundation
- Schemata.Tenancy.Caching -> Schemata.Caching.Skeleton
- Schemata.Tenancy.Foundation -> Schemata.Common, Schemata.Core, Schemata.Entity.Repository
- Schemata.Tenancy.Messaging -> Schemata.Messaging.Skeleton
- Schemata.Tenancy.Skeleton -> Schemata.Abstractions
- Schemata.Transport.Grpc -> Schemata.Common, Schemata.Core, Schemata.Messaging.Skeleton, Schemata.Security.Foundation
- Schemata.Transport.Http -> Schemata.Common, Schemata.Core, Schemata.Messaging.Skeleton
- Schemata.Validation.FluentValidation -> Schemata.Abstractions, Schemata.Advice, Schemata.Messaging.Skeleton, Schemata.Security.Skeleton
- Schemata.Validation.Skeleton -> Schemata.Abstractions, Schemata.Advice