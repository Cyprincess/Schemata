using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Restart-durable executor for AIP-165 purge. The scheduler rebuilds it from the
///     persisted <see cref="PurgeOperationArgs" /> and runs it, so a purge survives a host
///     restart. The filter is recompiled here from the persisted string.
/// </summary>
/// <typeparam name="TEntity">The soft-deletable resource entity type.</typeparam>
/// <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>
public sealed class PurgeOperationHandler<TEntity> : IOperationHandler<PurgeOperationArgs>
    where TEntity : class, ICanonicalName, ISoftDelete
{
    private const int SampleLimit = 100;

    private readonly IServiceProvider _services;

    public PurgeOperationHandler(IServiceProvider services) { _services = services; }

    #region IOperationHandler<PurgeOperationArgs> Members

    public async Task<object?> RunAsync(PurgeOperationArgs args, CancellationToken ct) {
        var filter = PurgeFilter.Compile<TEntity>(_services, args.Filter);
        return await ExecuteAsync(filter, args.Force, ct);
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
