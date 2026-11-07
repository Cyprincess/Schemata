# Accounting lifecycle

`QuotaAccountingService` in `src/Schemata.Quota.Foundation/Runtime/QuotaAccountingService.cs` owns reserve and settlement transactions. `RepositoryQuotaStore` joins five repositories to the same `IUnitOfWork`; it does not use `IResourceMutation` or a cache provider.

## Weighted reservation

A reservation contains one or more `QuotaCharge` values. Amount, limit, version, and burst are positive. Fixed-window, sliding-window, and token-bucket policies have a positive period. Concurrency reservations carry an expiration time.

The service groups duplicate `(Policy.Name, Policy.Version, Metric.Value)` charges, checks their combined amount, and sorts them with ordinal string comparisons. It resolves every keyed algorithm before writing. A rejection rolls back accounts, segments, reservations, items, and ledger rows for every metric in the operation.

## Idempotency

`QuotaRequestFingerprint` hashes the normalized scope, subject, sorted charges and policy values, and expiration with SHA-256. The reservation primary key is `(Scope, Subject, OperationId)`.

A matching operation ID and fingerprint returns the existing reservation without applying its charges again. A different fingerprint throws `AlreadyExistsException`. The account key `(Scope, Subject, Metric, PolicyName, PolicyVersion)` and the reservation key arbitrate first-insert races at the database.

## Transactions and concurrency

An independent reserve or settlement begins with `IsolationLevel.Serializable`. A caller-enlisted operation joins its supplied unit of work without changing the outer isolation level. Account and reservation saves use the loaded `Timestamp` values for provider optimistic concurrency. `RepositoryQuotaStore` rejects an empty loaded timestamp or an advisor-suppressed mandatory write with `FailedPreconditionException`.

A capacity decision checks `committed + reserved + requested` with checked arithmetic. Account CAS losses and primary-key collisions roll back the complete operation. After successful cleanup, a settlement CAS loser reads the reservation in a fresh scope and returns an already-settled result without appending another ledger batch.

Owned reserve and settlement transactions attempt disposal once. If operation and disposal both
fail, `AggregateException` carries the operation exception first and cleanup exception second.
Cleanup failure propagates before settlement readback; unknown transaction evidence stays unknown.

## Settlement and ledger

`QuotaReservationSettlement` is the only owner of commit, release, and expire transitions. It loads each reservation item, resolves its snapshotted algorithm, applies the algorithm transition, saves account and segment state, updates the reservation through CAS, and appends the ledger batch.

Reservation items persist their originating window and allocation UID. Fixed-window settlement
changes usage only in that window; sliding-window settlement changes only its own segment. Late
settlement of an expired allocation preserves newer reservations, including out-of-order release.

The ledger is append-only. Each row records reservation UID, account UID, operation ID, settlement kind, amount, and creation time. Metric filtering selects matching item accounts before ordering and paging. `QuotaLedgerQuery.PageSize` accepts values from 1 through 200.

## Expiry

Every reserve first settles expired pending reservations in the current transaction. This lazy path recovers abandoned leases even when `IScheduler` is absent.

When a reservation has an expiration and the current services contain `IScheduler`, reserve adds a domain commit sink. After a successful commit, the sink triggers `QuotaExpiryJob` with the expiration as its one-shot start time. The job calls `IQuotaAccounting.ExpireAsync` and enters the same settlement owner. A rollback creates no scheduler trigger.
