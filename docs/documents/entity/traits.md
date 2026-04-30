# Traits

Traits are marker interfaces that add cross-cutting behavior to entities and DTOs.
When an entity or DTO implements a trait, Schemata's built-in advisors detect it at
runtime and apply the corresponding logic automatically within the
[mutation pipeline](../repository/mutation-pipeline.md).

Traits live in two packages depending on where they are applied:

| Package                          | Applied to            | Traits                                                                                                                                |
| -------------------------------- | --------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Abstractions.Entities` | Entity classes        | IIdentifier, ITimestamp, ISoftDelete, IConcurrency, ICanonicalName, IStateful, IDescriptive, IExpiration, IEvent, IOwnable, IOrdering |
| `Schemata.Abstractions.Resource` | Request/response DTOs | IFreshness, IUpdateMask, IValidation, IRequestIdentification                                                                          |

---

## Entity traits

These interfaces are implemented on entity classes persisted through the repository.

### IIdentifier

Gives an entity a unique numeric identifier.

```csharp
public interface IIdentifier
{
    long Id { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:** None. The `Id` value is typically assigned by the database or set
explicitly by application code.

---

### ITimestamp

Tracks when an entity was created and last updated.

```csharp
public interface ITimestamp
{
    DateTime? CreateTime { get; set; }
    DateTime? UpdateTime { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:**

| Advisor                          | Pipeline | Behavior                                                      |
| -------------------------------- | -------- | ------------------------------------------------------------- |
| `AdviceAddTimestamp<TEntity>`    | Add      | Sets both `CreateTime` and `UpdateTime` to `DateTime.UtcNow`. |
| `AdviceUpdateTimestamp<TEntity>` | Update   | Sets `UpdateTime` to `DateTime.UtcNow`.                       |

Both advisors are suppressed when `SuppressTimestamp` is present in the advice context.

---

### ISoftDelete

Enables soft deletion. Instead of physically removing a row, the entity is flagged
with a deletion timestamp and an optional scheduled purge time.

```csharp
public interface ISoftDelete
{
    DateTime? DeleteTime { get; set; }
    DateTime? PurgeTime  { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:**

| Advisor                               | Pipeline | Behavior                                                                                                                                                                     |
| ------------------------------------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AdviceAddSoftDelete<TEntity>`        | Add      | Clears `DeleteTime` to `null` so newly added entities are never marked as deleted.                                                                                           |
| `AdviceRemoveSoftDelete<TEntity>`     | Remove   | Intercepts the physical delete: sets `DeleteTime` to `DateTime.UtcNow`, calls `UpdateAsync` on the repository, and returns `AdviseResult.Handle` to prevent the real delete. |
| `AdviceBuildQuerySoftDelete<TEntity>` | Query    | Applies a global filter excluding entities where `DeleteTime` is non-null.                                                                                                   |

All three advisors are suppressed when `SuppressSoftDelete` is present in the advice
context. The query filter has its own suppression flag, `SuppressQuerySoftDelete`.

---

### IConcurrency

Provides optimistic concurrency control via a GUID-based version token.

```csharp
public interface IConcurrency
{
    Guid? Timestamp { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:**

| Advisor                            | Pipeline | Behavior                                                                                                                               |
| ---------------------------------- | -------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `AdviceAddConcurrency<TEntity>`    | Add      | Generates a new `Guid` and assigns it to `Timestamp`.                                                                                  |
| `AdviceUpdateConcurrency<TEntity>` | Update   | Loads the stored entity, compares `Timestamp` values, and throws `ConcurrencyException` on mismatch. On success, assigns a new `Guid`. |

Both advisors are suppressed when `SuppressConcurrency` is present in the advice context.

IConcurrency is also the entity-side counterpart of the [IFreshness](#ifreshness) resource
trait. The `FreshnessHelper` computes weak ETags by base64url-encoding the `Timestamp`
GUID bytes.

---

### ICanonicalName

Gives an entity a short name and a fully-qualified canonical resource name following the
AIP-122 pattern (e.g., `publishers/acme/books/les-miserables`).

```csharp
public interface ICanonicalName
{
    string? Name          { get; set; }
    string? CanonicalName { get; set; }
}
```

The resource name pattern is declared on the entity class with the `[CanonicalName]`
attribute:

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { ... }
```

**Applies to:** Entity

**Built-in advisors:**

| Advisor                           | Pipeline | Behavior                                                                                                                               |
| --------------------------------- | -------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `AdviceAddCanonicalName<TEntity>` | Add      | Reads the `[CanonicalName]` attribute, resolves the pattern against the entity's properties, and writes the result to `CanonicalName`. |

---

### IOwnable

Records the principal that owns the entity, enabling owner-scoped query filtering and
authorization.

```csharp
public interface IOwnable
{
    string? Owner { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:** None in the core repository package. The `IRepository` exposes
`SuppressOwner()` and `SuppressQueryOwner()` that place `OwnerSuppressed` and
`QueryOwnerSuppressed` markers in the advice context. Application-level advisors
can implement `IRepositoryAddAdvisor<TEntity>` and
`IRepositoryBuildQueryAdvisor<TEntity>` to auto-assign owners and scope queries by
reading the current principal from `AdviceContext.ServiceProvider`.

---

### IStateful

Indicates that an entity has a discrete state representing a workflow or lifecycle stage.

```csharp
public interface IStateful
{
    string? State { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:** None from the core repository package. The workflow module
(`Schemata.Workflow.Foundation`) uses `State` when processing state transitions.

---

### IDescriptive

Provides user-facing display names and descriptions with optional localization,
corresponding to AIP-148 `display_name` and `description`.

```csharp
public interface IDescriptive
{
    string?                     DisplayName  { get; set; }
    Dictionary<string, string>? DisplayNames { get; set; }
    string?                     Description  { get; set; }
    Dictionary<string, string>? Descriptions { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:** None. Display names and descriptions are set by application code
during create and update operations. `DisplayNames` and `Descriptions` are
dictionaries keyed by IETF BCP 47 language tag (e.g., `"en"`, `"zh-Hans"`).

---

### IExpiration

Marks an entity that can expire at a scheduled time.

```csharp
public interface IExpiration
{
    DateTime? ExpireTime { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:** None. Application code is responsible for setting and acting on
`ExpireTime`.

---

### IEvent

Marks an entity as an audit event or state-change log entry. Carries the event type,
an optional note, and the identity of the user who triggered the event.

```csharp
public interface IEvent
{
    string  Event       { get; set; }
    string? Note        { get; set; }
    long?   UpdatedById { get; set; }
    string? UpdatedBy   { get; set; }
}
```

**Applies to:** Entity

**Built-in advisors:** None. Events are typically created by application code or
workflow advisors when recording state changes.

---

## Resource traits

These interfaces are implemented on request or response DTOs exchanged through the
[resource layer](../resource/overview.md).

### IFreshness

Carries an HTTP ETag for conditional requests (`If-Match` / `If-None-Match`).

```csharp
public interface IFreshness
{
    string? EntityTag { get; set; }
}
```

**Applies to:** Request/Response DTO

**Built-in advisors:**

| Advisor                                     | Pipeline       | Behavior                                                                                                                                   |
| ------------------------------------------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `AdviceUpdateFreshness<TEntity, TRequest>`  | Update request | Reads the ETag from the request, compares it against the entity's `IConcurrency.Timestamp`, and throws `ConcurrencyException` on mismatch. |
| `AdviceDeleteFreshness<TEntity>`            | Delete request | Same comparison for delete operations. Skipped when `DeleteRequest.Force` is `true`.                                                       |
| `AdviceResponseFreshness<TEntity, TDetail>` | Response       | Computes a weak ETag from the entity's `IConcurrency.Timestamp` and writes it to the detail DTO's `EntityTag`.                             |

All three advisors are suppressed when `SuppressFreshness` is present in the advice
context. IFreshness depends on the entity implementing [IConcurrency](#iconcurrency) --
without a `Timestamp` on the entity, the advisors short-circuit.

---

### IUpdateMask

Enables partial (field-mask) updates on a request DTO.

```csharp
public interface IUpdateMask
{
    string? UpdateMask { get; set; }
}
```

`UpdateMask` is a comma-separated list of field paths to update. When the request
implements this trait, the resource operation handler maps only the specified fields
from the request onto the entity. When `UpdateMask` is absent or `null`, all fields
are mapped.

**Applies to:** Request DTO (update)

**Built-in advisors:** None. The field-mask logic is handled directly by the
`ResourceOperationHandler` during the update mapping step, not through a separate
advisor.

---

### IValidation

Enables validation-only (dry-run) mode on a request.

```csharp
public interface IValidation
{
    bool ValidateOnly { get; set; }
}
```

When `ValidateOnly` is `true`, the request passes through all validation advisors and
then throws `NoContentException` to signal a successful dry run without persisting
any changes.

**Applies to:** Request DTO (create and update)

**Built-in advisors:**

| Advisor                                            | Pipeline       | Behavior                                                                                             |
| -------------------------------------------------- | -------------- | ---------------------------------------------------------------------------------------------------- |
| `AdviceCreateRequestValidation<TEntity, TRequest>` | Create request | Runs validation; if `ValidateOnly` is `true`, throws `NoContentException` after validation succeeds. |
| `AdviceUpdateRequestValidation<TEntity, TRequest>` | Update request | Same behavior for update operations.                                                                 |

---

### IRequestIdentification

Carries a unique request identifier for idempotent create operations.

```csharp
public interface IRequestIdentification
{
    string? RequestId { get; set; }
}
```

When a create request includes a `RequestId`, the framework checks the
`IIdempotencyStore` for a cached result. If a previous result exists, it is returned
immediately without re-executing the create. Otherwise, the new result is stored
after creation for future duplicate detection.

**Applies to:** Request DTO (create)

**Built-in advisors:**

| Advisor                                                      | Pipeline       | Behavior                                                                                                                                                               |
| ------------------------------------------------------------ | -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail>` | Create request | Checks the `IIdempotencyStore` for a cached result matching `RequestId`. Returns the cached result if found; otherwise stores a pending key for post-creation caching. |
| `AdviceResponseIdempotency<TEntity, TDetail>`                | Response       | Persists the create result into the `IIdempotencyStore` when a pending idempotency key exists.                                                                         |

Suppressed when `SuppressCreateIdempotency` is present in the advice context.

---

## Common trait combinations

Most entities combine several traits to get a standard behavior set. Here are typical
combinations:

### Basic entity

`IIdentifier` + `ITimestamp` + `ISoftDelete`

Provides auto-incrementing IDs, automatic timestamps on create/update, and soft deletion
with query filtering. This is the minimal recommended set for most business entities.

### Concurrency-safe entity

`IIdentifier` + `ITimestamp` + `ISoftDelete` + `IConcurrency`

Adds optimistic concurrency control on top of the basic set. Required when the entity's
response DTO implements `IFreshness` for ETag-based conditional requests.

### Named resource

`IIdentifier` + `ITimestamp` + `ISoftDelete` + `IConcurrency` + `ICanonicalName`

Adds a human-readable `Name` and auto-resolved `CanonicalName` following the AIP-122
resource name pattern. This is the standard combination for resources exposed through
the resource layer.
