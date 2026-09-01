using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

    private readonly IRepository<TEntity> _repository;
    private readonly IServiceProvider     _services;

    /// <summary>Initializes the durable purge executor.</summary>
    /// <param name="repository">The repository of the purged resource.</param>
    /// <param name="services">The service provider for resolving expression compilers.</param>
    public PurgeJob(IRepository<TEntity> repository, IServiceProvider services) {
        _repository = repository;
        _services   = services;
    }

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var args = context.ArgsJson is { } json
            ? JsonSerializer.Deserialize<PurgeOperationArgs>(json, SchemataJson.Default)
            : null;

        var filter = PurgeFilter.Compile<TEntity>(_services, args?.Filter, args?.Language);
        var result = await ExecuteAsync(filter, args?.Parent, args?.Force ?? false, ct);

        if (context.Execution is { } execution) {
            execution.Output = JsonSerializer.Serialize(result, SchemataJson.Default);
        }
    }

    #endregion

    private async Task<PurgeResponse> ExecuteAsync(
        Expression<Func<TEntity, bool>>? filter,
        string?                          parent,
        bool                             force,
        CancellationToken                ct
    ) {
        var container  = new ResourceRequestContainer<TEntity>();
        ResourceIdentifiers.ApplyParent(container, parent);

        var result = new PurgeResponse();
        using (_repository.SuppressQuerySoftDelete()) {
            result.PurgeCount = await _repository.LongCountAsync(Query, ct);
        }

        if (!force) {
            using (_repository.SuppressQuerySoftDelete()) {
                await foreach (var row in _repository.ListAsync(q => Query(q).Take(SampleLimit), ct)) {
                    var item = row.CanonicalName;
                    if (!string.IsNullOrWhiteSpace(item)) {
                        result.PurgeSample.Add(item);
                    }
                }
            }

            return result;
        }

        using (_repository.SuppressQuerySoftDelete()) {
            await foreach (var row in _repository.ListAsync(Query, ct)) {
                using var removeSuppression = _repository.SuppressSoftDelete();
                await _repository.RemoveAsync(row, ct);
            }
        }

        await _repository.CommitAsync(ct);

        return result;

        IQueryable<TEntity> Query(IQueryable<TEntity> q) {
            var eligible = container.Query(q.Where(row => row.DeleteTime != null));
            return filter is null ? eligible : eligible.Where(filter);
        }
    }
}
