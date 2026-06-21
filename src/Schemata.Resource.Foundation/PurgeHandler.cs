using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Built-in AIP-165 handler that dispatches purge as a long-running operation. The work runs in
///     <see cref="PurgeJob{TEntity}" />, triggered through the scheduler and rebuilt from the
///     persisted request data so it survives a host restart and is managed through the standard
///     <c>operations/{operation}</c> surface (get / list / :cancel / :wait).
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>
public sealed class PurgeHandler<TEntity> : IResourceMethodHandler<TEntity, PurgeRequest, Operation>
    where TEntity : class, ICanonicalName, ISoftDelete
{
    private readonly IServiceProvider _services;

    /// <summary>Initializes the built-in purge handler.</summary>
    /// <param name="services">Service provider for resolving the scheduler and compiler.</param>
    public PurgeHandler(IServiceProvider services) { _services = services; }

    public async ValueTask<Operation> InvokeAsync(
        string?           name,
        PurgeRequest      request,
        TEntity?          entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        // Validate the filter up front so a malformed request fails fast with INVALID_ARGUMENT.
        // The compiled expression is discarded; the durable job recompiles it from the persisted
        // filter string at execution time.
        _ = PurgeFilter.Compile<TEntity>(_services, request.Filter);

        var scheduler = _services.GetService<IScheduler>()
                     ?? throw new InvalidOperationException("Purge requires a scheduler.");

        var uid        = Identifiers.NewUid();
        var collection = ResourceNameDescriptor.ForType<SchemataJobExecution>().Collection;
        var argsJson   = JsonSerializer.Serialize(
            new PurgeOperationArgs { Filter = request.Filter, Force = request.Force },
            SchemataJson.Default);

        var context = new JobContext {
            Job          = $"{collection}/{uid:n}:{Verbs.Purge}",
            ExecutionUid = uid,
            Method       = Verbs.Purge,
            ArgsJson     = argsJson,
        };

        var execution = await scheduler.TriggerAsync<PurgeJob<TEntity>>(context, ct);

        return OperationMapper.FromExecution(execution);
    }
}
