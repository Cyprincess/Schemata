# Subscriptions

`SchemataPushSubscription` is the addressing table a transport queries to resolve where to deliver a
`RecipientTarget`. It is modeled after ASP.NET Core Identity `AspNetUserLogins`: an owner is bound to
a transport endpoint, unique by `(owner, provider, providerKey)`. `IPushSubscriptionManager` and
`DefaultPushSubscriptionManager` manage the rows.

## The entity

`Schemata.Push.Skeleton.Entities.SchemataPushSubscription`:

```csharp
[DisplayName("PushSubscription")]
[Table("SchemataPushSubscriptions")]
[CanonicalName("pushSubscriptions/{push_subscription}")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(Owner), nameof(Provider), nameof(ProviderKey), IsUnique = true)]
public class SchemataPushSubscription : IIdentifier, ICanonicalName, IOwnable, IConcurrency,
                                        IDescriptive, ISoftDelete, ITimestamp
{
    public virtual string? Provider    { get; set; }   // "fcm", "apns", "webhook", ...
    public virtual string? ProviderKey { get; set; }   // device token, URL, address
    public virtual string? Metadata    { get; set; }   // transport-specific JSON
    // Owner, Name, CanonicalName, Uid, Timestamp, DisplayName, Description,
    // DeleteTime, PurgeTime, CreateTime, UpdateTime from the traits
}
```

The resource name is `pushSubscriptions/{push_subscription}` — a top-level resource. The owner
relationship is carried by `IOwnable.Owner` as a free-form canonical name, so the same table
addresses users, groups, tags, or any principal. The unique index on
`(Owner, Provider, ProviderKey)` enforces one endpoint per owner per transport.

### Trait behaviour

Because the entity implements the standard traits, the repository advisor pipeline activates by trait
detection once a persistence provider is configured:

| Trait          | Advisor behaviour                                                       |
| -------------- | ----------------------------------------------------------------------- |
| `ITimestamp`   | `CreateTime` / `UpdateTime` stamped on add and update                   |
| `ISoftDelete`  | delete becomes a soft delete; queries exclude soft-deleted rows         |
| `IConcurrency` | `Timestamp` drives the AIP-154 etag                                     |
| `IOwnable`     | ownership advisors apply when the host enabled `UseOwner()` (see below) |

The timestamp, soft-delete, and concurrency advisors detect their traits on the entity itself, so
implementing the interface is the whole registration.

## IPushSubscriptionManager

```csharp
public interface IPushSubscriptionManager
{
    IAsyncEnumerable<SchemataPushSubscription> GetForOwnerAsync(
        string owner, string? provider = null, CancellationToken ct = default);

    ValueTask<SchemataPushSubscription> AddAsync(
        string owner, string provider, string providerKey,
        string? metadata = null, CancellationToken ct = default);

    ValueTask RemoveAsync(
        string owner, string provider, string providerKey, CancellationToken ct = default);

    ValueTask<bool> ExistsAsync(
        string owner, string provider, string providerKey, CancellationToken ct = default);
}
```

`DefaultPushSubscriptionManager` wraps `IRepository<SchemataPushSubscription>`:

| Method             | Behaviour                                                                                           |
| ------------------ | --------------------------------------------------------------------------------------------------- |
| `GetForOwnerAsync` | `ListAsync` over `Owner == owner && (provider == null \|\| Provider == provider)`                   |
| `AddAsync`         | idempotent on the triple: returns the existing row if present, otherwise creates, adds, and commits |
| `RemoveAsync`      | finds the matching row and removes + commits; a no-op when absent                                   |
| `ExistsAsync`      | `AnyAsync` over the triple                                                                          |

`AddAsync` sets `Owner` explicitly from its argument and assigns a fresh `Uid`. Each mutating method
performs one repository mutation followed by `CommitAsync`, matching the manager pattern used across
the framework.

## Ownership interaction

The ownership advisors activate only when the host calls `repository.UseOwner()`. With ownership
enabled and `SchemataPushSubscription` queried through the repository:

- `AddAsync` sets `Owner` explicitly, so `AdviceAddOwner` preserves the manager's owner.
- Queries gain an `Owner == currentPrincipalOwner` filter from `AdviceBuildQueryOwner`, on top of the
  manager's `Owner == owner` predicate.

`UseOwner()` defaults to `OnNullOwnerPolicy.Reject`: an unauthenticated query returns empty, and an
unowned add throws. For administrative paths that manage subscriptions across owners, either disable
ownership for this entity, scope the call inside `SuppressQueryOwner()`, run under the matching
principal, or register an `IOwnerResolver<SchemataPushSubscription>`.

## Resource exposure

`SchemataPushFeature` registers `SchemataPushSubscription` as a resource. The BREAD endpoints appear
when the host activates a resource transport (`Schemata.Resource.Http` / `Schemata.Resource.Grpc`):

```text
GET    /v1/pushSubscriptions
POST   /v1/pushSubscriptions
GET    /v1/pushSubscriptions/{push_subscription}
PATCH  /v1/pushSubscriptions/{push_subscription}
DELETE /v1/pushSubscriptions/{push_subscription}
```

Delete is a soft delete (`ISoftDelete`); the list excludes soft-deleted rows by default.

## Transport opt-in

The subscription table is optional per transport. A transport that holds its own connection state
(a SignalR hub) or delegates to a third-party SDK (FCM, APNS) can ignore it. A transport that needs
durable endpoint storage (a webhook URL, a device token) queries `IPushSubscriptionManager` for the
owner addressed by a `RecipientTarget`.

## See also

- [Overview](overview.md) — packages, startup, and the builder
- [Dispatch](dispatch.md) — how a `RecipientTarget` reaches a transport
- [Ownership](../repository/ownership.md) — the owner advisor pipeline and resolver contract
- [Traits](../entity/traits.md) — the standard entity traits
