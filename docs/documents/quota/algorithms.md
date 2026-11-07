# Quota algorithms

`Schemata.Quota.Foundation/Algorithms` contains four keyed `IQuotaAlgorithm` implementations. Evaluation returns an account and segment transition without writing persistence. Settlement returns a new transition for commit, release, or expiry.

## Fixed window

Key: `QuotaConstants.Algorithms.FixedWindow`.

The account window starts at `WindowStart`. Evaluation at or after the next boundary discards committed and reserved usage from the previous period. Allowed requests increase reserved usage; commit moves that amount to committed usage, while release and expiry remove it. Rejection returns the delay to the next boundary.

## Sliding window

Key: `QuotaConstants.Algorithms.SlidingWindow`.

Each reservation creates a segment with start and expiration times. Evaluation removes segments whose expiration is at or before the current time, then sums live committed and reserved amounts. Rejection walks segments by expiration until their released amount covers the deficit.

Commit converts live reserved segment amounts to committed amounts. Release and expiry subtract live reserved amounts. Expired segments cannot contribute new consumption during settlement.

## Token bucket

Key: `QuotaConstants.Algorithms.TokenBucket`.

The bucket starts with `Burst` tokens. Elapsed whole periods add `Limit` tokens per period and cap the balance at `Burst`; fractional periods remain for the next evaluation. An allowed request subtracts its weighted amount immediately.

Commit leaves the consumed balance unchanged. Release and expiry refund the reservation amount up to the burst cap. Rejection computes the whole periods needed to refill the deficit.

## Concurrency

Key: `QuotaConstants.Algorithms.Concurrency`.

Reserved usage counts pending in-flight leases. Every concurrency reservation has an expiration so lazy or scheduled recovery can remove an abandoned lease. Commit, release, and expiry remove the lease without adding period consumption. Rejection returns a zero retry delay because the algorithm context contains no lease timestamps.

All four algorithms accept usage exactly at the limit and reject the next unit. Checked arithmetic rejects values whose summed usage exceeds `long` range.
