using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Repository;

/// <summary>
///     Default <see cref="IFlowEffectRecorder" /> over the Schemata repository persistence
///     (<see cref="IRepository{TEntity}" />). Store access enlists in the flow transition's unit of
///     work through <see cref="IRepository.Join" />, so the intent row commits with the transition and
///     rolls back with it. The persistence engine is whichever repository provider the application
///     installed for <see cref="SchemataFlowEffectIntent" />; nothing is serialized in-process.
/// </summary>
public sealed class RepositoryFlowEffectRecorder(
    IRepository<SchemataFlowEffectIntent>       repository,
    IResourceMutation<SchemataFlowEffectIntent> mutation
) : IFlowEffectRecorder
{
    // Rows staged earlier in the same unit of work are not queryable yet on every provider (EF Core
    // flushes its change tracker at save), so settle resolves rows recorded by this instance locally
    // before falling back to a store query.
    private readonly Dictionary<string, SchemataFlowEffectIntent> _staged = new(StringComparer.Ordinal);

    #region IFlowEffectRecorder Members

    public async ValueTask<long> CountIntentsAsync(string token, CancellationToken ct) {
        return await repository.LongCountAsync(query => query.Where(row => row.Token == token), ct);
    }

    public async ValueTask<FlowEffectIntent> RecordIntentAsync(
        FlowEffectIntent intent,
        IUnitOfWork      unitOfWork,
        CancellationToken ct
    ) {
        repository.Join(unitOfWork);
        var existing = await repository.FirstOrDefaultAsync(
            query => query.Where(row => row.RequestId == intent.RequestId), ct);
        if (existing is not null) {
            return new() {
                RequestId = existing.RequestId,
                Process   = existing.Process,
                Token     = existing.Token,
                Task      = existing.Task,
                State     = existing.State,
            };
        }

        var row = new SchemataFlowEffectIntent {
            RequestId = intent.RequestId,
            Process   = intent.Process,
            Token     = intent.Token,
            Task      = intent.Task,
            State     = FlowEffectState.Recorded,
        };
        await mutation.CreateAsync(row, unitOfWork, ct);
        _staged[intent.RequestId] = row;
        return intent;
    }

    public ValueTask RecordCompletionAsync(string requestId, IUnitOfWork unitOfWork, CancellationToken ct) {
        return new(SettleAsync(requestId, FlowEffectState.Completed, unitOfWork, ct));
    }

    public ValueTask RecordOutcomeUnknownAsync(string requestId, IUnitOfWork unitOfWork, CancellationToken ct) {
        return new(SettleAsync(requestId, FlowEffectState.OutcomeUnknown, unitOfWork, ct));
    }

    #endregion

    private async Task SettleAsync(string requestId, FlowEffectState state, IUnitOfWork unitOfWork, CancellationToken ct) {
        repository.Join(unitOfWork);
        var row = _staged.TryGetValue(requestId, out var staged)
            ? staged
            : await repository.FirstOrDefaultAsync(query => query.Where(r => r.RequestId == requestId), ct);
        if (row is null) {
            return;
        }

        row.State = state;
        await mutation.UpdateAsync(row, unitOfWork, ct: ct);
    }
}
