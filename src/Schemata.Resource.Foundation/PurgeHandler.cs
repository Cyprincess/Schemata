using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Built-in AIP-165 handler that dispatches purge as a long-running operation. The work runs in
///     <see cref="PurgeJob{TEntity}" />, triggered through the scheduler and rebuilt from the persisted
///     request data so it survives a host restart and is managed through the standard
///     <c>operations/{operation}</c> surface (get / list / :cancel / :wait).
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>
public sealed class PurgeHandler<TEntity> : IRequestHandler<PurgeResourceRequest<TEntity>, Operation>
    where TEntity : class, ICanonicalName, ISoftDelete
{
    private readonly IServiceProvider _services;

    /// <summary>Initializes the built-in purge handler.</summary>
    /// <param name="services">Service provider for resolving the scheduler and compiler.</param>
    public PurgeHandler(IServiceProvider services) { _services = services; }

    public async Task<Operation> HandleAsync(
        PurgeResourceRequest<TEntity> request,
        CancellationToken             ct = default
    ) {
        _ = PurgeFilter.Compile<TEntity>(_services, request.Filter, request.Language);

        var scheduler = _services.GetService<IScheduler>();
        if (scheduler is null) {
            throw new InvalidOperationException("Purge requires a scheduler.");
        }

        var uid = Identifiers.NewUid();
        var args = JsonSerializer.Serialize(new PurgeOperationArgs {
            Filter   = request.Filter,
            Language = request.Language,
            Parent   = request.Parent,
            Force    = request.Force,
        }, SchemataJson.Default);

        var context = new JobContext {
            ExecutionUid = uid,
            Method       = Verbs.Purge,
            ArgsJson     = args,
        };

        var execution = await scheduler.TriggerAsync<PurgeJob<TEntity>>(context, ct);

        return OperationMapper.FromExecution(execution);
    }
}
