using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Services;

/// <summary>Stages generated credentials and atomically advances the active row for one parent and usage.</summary>
/// <typeparam name="TSecurity">Concrete security entity type.</typeparam>
public sealed class SecurityKeyRotationService<TSecurity>(
    ISecurityStore<TSecurity> securities,
    ISecurityKeyGenerator<TSecurity> generator,
    IResourceMutation<TSecurity> mutation,
    IServiceProvider services,
    TimeProvider? time = null
) where TSecurity : SchemataSecurity, new()
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Publishes a staged credential before switching the active row in one serializable transaction.</summary>
    /// <param name="parent">Canonical parent owning the key slot.</param>
    /// <param name="usage">Purpose whose active credential rotates.</param>
    /// <param name="algorithm">Stored algorithm requested from the generator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The activated credential.</returns>
    /// <exception cref="AbortedException">The active row changed after its concurrency stamp was captured.</exception>
    public async Task<TSecurity> RotateAsync(string parent, string usage, string algorithm, CancellationToken ct) {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(usage);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);

        var previous = await FindActiveAsync(parent, usage, ct);
        var previousUid = previous?.Uid;
        var previousTimestamp = previous?.Timestamp;
        var generated = await generator.GenerateAsync(new(parent, usage, algorithm), ct)
                     ?? throw new InvalidOperationException("The security key generator returned no credential.");
        generated.Parent = parent;
        generated.Usage = usage;
        generated.Algorithm = algorithm;
        generated.Status = SecurityConstants.Statuses.Staged;
        generated.ActivationTime = null;
        generated.RetirementTime = null;
        var staged = await securities.CreateAsync(generated, ct)
                  ?? throw new InvalidOperationException("The staged security key was not created.");

        await using var repository = services.GetRequiredService<IRepository<TSecurity>>();
        await using var transaction = repository.Begin(IsolationLevel.Serializable);
        var pending = await repository.SingleOrDefaultAsync(
            query => query.Where(row => row.Uid == staged.Uid), ct);
        if (pending?.Status != SecurityConstants.Statuses.Staged) {
            throw new InvalidOperationException("The staged security key is not available for activation.");
        }

        var current = await repository.FirstOrDefaultAsync(query => query
            .Where(row => row.Parent == parent && row.Usage == usage && row.Status == SecurityConstants.Statuses.Valid)
            .OrderByDescending(row => row.CreateTime)
            .ThenBy(row => row.Key), ct);
        if (current?.Uid != previousUid || current?.Timestamp != previousTimestamp) {
            throw new AbortedException();
        }

        var now = _time.GetUtcNow().UtcDateTime;
        pending.Status = SecurityConstants.Statuses.Valid;
        pending.ActivationTime = now;
        await RequireApplied(mutation.UpdateAsync(pending, transaction, Operations.Update, ct));

        if (current is not null) {
            if (previousTimestamp is { } timestamp) {
                current.Timestamp = timestamp;
            }

            current.Status = SecurityConstants.Statuses.Retired;
            current.RetirementTime = now;
            await RequireApplied(mutation.UpdateAsync(current, transaction, Operations.Update, ct));
        }

        await transaction.CommitAsync(ct);
        return pending;
    }

    private async Task<TSecurity?> FindActiveAsync(string parent, string usage, CancellationToken ct) {
        await foreach (var row in securities.ListByParentAsync(
                           parent, usage: usage, status: SecurityConstants.Statuses.Valid, ct: ct)) {
            return row;
        }

        return null;
    }

    private static async Task RequireApplied(Task<MutationResult> update) {
        if (await update != MutationResult.Applied) {
            throw new InvalidOperationException("The security key transition was not staged.");
        }
    }
}
