# Quota accounting

`Schemata.Quota.Skeleton` defines provider-neutral accounts, reservations, policies, charges, and settlement contracts. `Schemata.Quota.Foundation` implements those contracts through `IQuotaAccounting` and repository persistence. Activate the runtime with `UseQuotaAccounting()` on `SchemataBuilder`; the implementation is in `src/Schemata.Quota.Foundation/Extensions/SchemataBuilderExtensions.cs`.

Durable accounting records weighted operations in accounts and reservation items. Callers reserve one or more metrics, then commit, release, or expire the reservation through `IQuotaAccounting`. Each reservation item stores its policy snapshot so a later service scope can settle the original algorithm and limits.

## Scope and subject

`QuotaScope`, `QuotaSubject`, and `QuotaMetric` are opaque values. A local caller can supply a scope explicitly. When `QuotaReservationRequest.Scope` is null, `QuotaAccountingService` resolves `IQuotaScopeResolver` from the current services before opening a transaction. The host registers that resolver; the Quota feature registers no default.

`PrincipalQuotaSubjectResolver` reads `sub` and then `ClaimTypes.NameIdentifier` from an authenticated identity. A requested subject must match the authenticated value. Consumers that authorize service delegation replace `IQuotaSubjectResolver` in DI. Scope selection and subject delegation are separate decisions.

## Native rate limiting

`UseRateLimiting()` in `Schemata.Core` activates `SchemataRateLimitingFeature`, which runs the ASP.NET Core in-process limiter. It retains `AddSchemataRateLimiter`, `QuotaMetadata`, `RateLimitPolicyAttribute`, and native rejection details.

`UseQuotaAccounting()` activates durable accounts and reservations in `Schemata.Quota.Foundation`. The two features have separate builders, persistence requirements, and runtime behavior.

See [Accounting](accounting.md) for transactions and idempotency, [Algorithms](algorithms.md) for capacity calculations, and [Transports](transports.md) for authenticated HTTP and gRPC controls.
