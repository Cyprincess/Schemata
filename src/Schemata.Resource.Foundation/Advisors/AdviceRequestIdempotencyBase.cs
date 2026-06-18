using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

public abstract class AdviceRequestIdempotencyBase<TEntity, TRequest, TPayload>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TPayload : class, ICanonicalName
{
    private readonly ICacheProvider _cache;
    private readonly TimeProvider   _time;

    protected AdviceRequestIdempotencyBase(ICacheProvider cache, TimeProvider? timeProvider = null) {
        _cache = cache;
        _time  = timeProvider ?? TimeProvider.System;
    }

    protected abstract string? Operation(AdviceContext ctx);

    protected abstract bool IsSuppressed(AdviceContext ctx);

    protected abstract void StoreReplay(AdviceContext ctx, TPayload payload);

    protected async Task<AdviseResult> AdviseCoreAsync(
        AdviceContext     ctx,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        if (request is not IRequestIdentification { RequestId: { Length: > 0 } requestId }) {
            return AdviseResult.Continue;
        }

        if (IsSuppressed(ctx)) {
            return AdviseResult.Continue;
        }

        var operation = Operation(ctx);
        if (operation is null) {
            return AdviseResult.Continue;
        }

        var options       = IdempotencyHelper.ResolveOptions(ctx.ServiceProvider);
        var principalId   = IdempotencyHelper.PrincipalId(principal);
        var payloadHash   = IdempotencyHelper.HashPayload(request);
        var canonicalName = request.CanonicalName ?? request.Name ?? string.Empty;

        var key = new PendingIdempotencyKey(
            requestId,
            operation,
            typeof(TEntity).FullName!,
            principalId,
            canonicalName,
            payloadHash).ToCacheKey();

        var (found, payload) = await IdempotencyHelper.ReadDoneAsync<TPayload>(_cache, key, payloadHash, ct);
        if (found && payload is not null) {
            StoreReplay(ctx, payload);
            return AdviseResult.Handle;
        }

        var record = new PendingIdempotencyRecord {
            OwnerToken    = Identifiers.NewUid().ToString("n"),
            Operation     = operation,
            RequestId     = requestId,
            Principal     = principalId,
            CanonicalName = canonicalName,
            PayloadHash   = payloadHash,
            CreateTime    = _time.GetUtcNow().UtcDateTime,
        };
        var recordBytes = JsonSerializer.SerializeToUtf8Bytes(record);

        var reserved = await _cache.TryAddAsync(key, recordBytes, new() {
            AbsoluteExpirationRelativeToNow = options.IdempotencyRetention,
        }, ct);

        if (!reserved) {
            var awaited = await IdempotencyHelper.AwaitDoneAsync<TPayload>(_cache, key, payloadHash, options.IdempotencyPendingWait, _time, ct);
            if (awaited is not null) {
                StoreReplay(ctx, awaited);
                return AdviseResult.Handle;
            }

            throw new ConcurrencyException();
        }

        ctx.Set(new IdempotencyReservation(key, payloadHash, recordBytes));
        return AdviseResult.Continue;
    }
}
