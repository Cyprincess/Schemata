using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Restart-durable executor for AIP-165 purge, dispatched as a scheduler job. The scheduler
///     rebuilds it from the persisted <see cref="PurgeOperationArgs" /> and runs it through the
///     standard execution pipeline, so a purge survives a host restart and is managed and observed
///     as an ordinary <c>operations/{operation}</c> long-running operation. The filter is recompiled
///     here from the persisted string.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>
public sealed class PurgeJob<TEntity> : IScheduledJob
    where TEntity : class, ICanonicalName, ISoftDelete
{
    private const int SampleLimit = 100;

    private readonly IServiceProvider _services;

    /// <summary>Initializes the durable purge executor.</summary>
    /// <param name="services">The service provider for resolving repositories and expression compilers.</param>
    public PurgeJob(IServiceProvider services) { _services = services; }

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var args = context.ArgsJson is { } json
            ? JsonSerializer.Deserialize<PurgeOperationArgs>(json, SchemataJson.Default)
            : null;

        var filter = PurgeFilter.Compile<TEntity>(_services, args?.Filter, args?.Language);
        var result = await ExecuteAsync(filter, args?.Force ?? false, ct);

        if (context.Execution is { } execution) {
            execution.Output = JsonSerializer.Serialize(result, SchemataJson.Default);
        }
    }

    #endregion

    private async Task<PurgeResponse> ExecuteAsync(
        Expression<Func<TEntity, bool>>? filter,
        bool                             force,
        CancellationToken                ct
    ) {
        var repository = _services.GetRequiredService<IRepository<TEntity>>();

        IQueryable<TEntity> Query(IQueryable<TEntity> q) {
            var eligible = q.Where(row => row.DeleteTime != null);
            return filter is null ? eligible : eligible.Where(filter);
        }

        var result = new PurgeResponse();
        using (repository.SuppressQuerySoftDelete()) {
            result.PurgeCount = await repository.LongCountAsync(Query, ct);
        }

        if (!force) {
            using (repository.SuppressQuerySoftDelete()) {
                await foreach (var row in repository.ListAsync(q => Query(q).Take(SampleLimit), ct)) {
                    var item = row.CanonicalName ?? row.Name;
                    if (!string.IsNullOrWhiteSpace(item)) {
                        result.PurgeSample.Add(item);
                    }
                }
            }

            return result;
        }

        using (repository.SuppressQuerySoftDelete()) {
            await foreach (var row in repository.ListAsync(Query, ct)) {
                using var removeSuppression = repository.SuppressSoftDelete();
                await repository.RemoveAsync(row, ct);
            }
        }

        await repository.CommitAsync(ct);

        return result;
    }
}
