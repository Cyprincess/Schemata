# Quota transports

`Schemata.Quota.Http` and `Schemata.Quota.Grpc` expose the accounting control plane through authenticated remote calls. Activate them with `UseQuotaAccounting().MapHttp().MapGrpc()` on the Schemata builder. The features depend on `SchemataQuotaAccountingFeature` and their respective shared transport features (`src/Schemata.Quota.Http/Features/SchemataQuotaHttpFeature.cs`, `src/Schemata.Quota.Grpc/Features/SchemataQuotaGrpcFeature.cs`).

## Trust boundary

Every operation requires an authenticated caller and one of these authorization policies: `quota.reserve`, `quota.commit`, `quota.release`, or `quota.ledger`. The controller and gRPC method metadata apply endpoint authorization, and `QuotaControlHandler` authorizes the same policy before invoking accounting (`src/Schemata.Quota.Http/Controllers/QuotaController.cs`, `src/Schemata.Quota.Grpc/QuotaGrpcMethodProvider.cs`, `src/Schemata.Quota.Foundation/Handlers/QuotaControlHandler.cs`).

The handler obtains scope only from the host's `IQuotaScopeResolver`. A missing or empty resolved scope fails with `FailedPreconditionException`. HTTP and gRPC request models carry `scope` and `tenant` fields only so `QuotaControlRequestGuard` can reject either supplied value with `InvalidArgumentException` before dispatch (`src/Schemata.Quota.Foundation/Handlers/QuotaControlRequestGuard.cs`).

`PrincipalQuotaSubjectResolver` reads `sub`, then `ClaimTypes.NameIdentifier`, from an authenticated identity. `requested_subject` must equal that value under the default resolver. A host that permits service delegation replaces `IQuotaSubjectResolver` with its delegation policy (`src/Schemata.Quota.Foundation/Runtime/PrincipalQuotaSubjectResolver.cs`). Commit and release pass the resolved scope and subject into settlement, which returns `NotFoundException` when the persisted reservation belongs to another caller boundary (`src/Schemata.Quota.Foundation/Runtime/QuotaReservationSettlement.cs`).

## Operations

| Operation | HTTP | gRPC `schemata.quota.v1.QuotaControl` |
| --- | --- | --- |
| Reserve | `POST /v1/quota:reserve` | `Reserve` |
| Commit | `POST /v1/quota:commit` | `Commit` |
| Release | `POST /v1/quota:release` | `Release` |
| List ledger | `GET /v1/quota/ledger` | `ListLedger` |

Reserve accepts an operation ID, weighted charges, an optional expiration, and an optional delegation subject. Commit and release accept a reservation UID. Ledger accepts an optional metric, an optional delegation subject, and a page size from 1 through 200. Responses contain reservation identity and state, account usage, retry-after data, or settlement ledger entries (`src/Schemata.Quota.Http/Models/QuotaControlModels.cs`, `src/Schemata.Quota.Grpc/Wire/QuotaControlWire.cs`).

The code-first service contract is `IQuotaGrpcService`. `QuotaGrpcModel` builds one protobuf-net model at `CompatibilityLevel.Level300` for server marshallers, clients, and reflection descriptors (`src/Schemata.Quota.Grpc/IQuotaGrpcService.cs`, `src/Schemata.Quota.Grpc/QuotaGrpcModel.cs`).

## Replay and transactions

Both transports dispatch the same control records to `QuotaControlHandler`. An operation ID replay with the same normalized scope, subject, charges, and expiration returns the original reservation. Reusing the operation ID with a different charge fingerprint returns `AlreadyExistsException` without applying more usage (`src/Schemata.Quota.Foundation/Runtime/QuotaAccountingService.cs`).

Every remote reserve, commit, and release calls `IQuotaAccounting` with `transaction: null`. Accounting therefore opens and commits its own serializable unit of work; a caller's unit of work cannot enlist or roll back a completed remote operation (`src/Schemata.Quota.Foundation/Handlers/QuotaControlHandler.cs`).

## Error parity

HTTP uses `UseSchemataExceptionHandler`, while gRPC uses the shared exception-mapping interceptor. Both preserve the accounting error classification. Quota exhaustion carries `QuotaFailureDetail` and `RetryInfoDetail`; HTTP emits the AIP error body and `Retry-After` header, and gRPC emits `RESOURCE_EXHAUSTED` with `google.rpc.QuotaFailure` and `google.rpc.RetryInfo` status details (`src/Schemata.Transport.Http/Extensions/ApplicationBuilderExtensions.cs`, `src/Schemata.Transport.Grpc/Proto/RpcStatusBuilder.cs`).

HTTP `Retry-After` delta-seconds is capped at `Int32.MaxValue` for client header-parser
interoperability. The structured `RetryInfoDetail` retains the full accounting delay.
