using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Concurrency;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     LINQ to DB implementation of <see cref="RepositoryBase{TEntity}" />.
/// </summary>
/// <remarks>
///     Mutations execute immediately against the data context inside an open transaction.
///     The transaction opens lazily on the first <see cref="AddAsync" /> / <see cref="UpdateAsync" />
///     / <see cref="RemoveAsync" /> when the repository owns its context; once enlisted via
///     <see cref="RepositoryBase{TEntity}.Join" />, the unit of work owns the transaction and
///     this repository executes statements against the shared connection.
/// </remarks>
/// <typeparam name="TContext">The <see cref="DataConnection" /> type.</typeparam>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public class LinqToDbRepository<TContext, TEntity> : RepositoryBase<TEntity>
    where TContext : DataConnection
    where TEntity : class
{
    private readonly Func<TContext>   _factory;
    private readonly ILogger?         _logger;
    private          TContext         _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LinqToDbRepository{TContext,TEntity}" /> class.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="factory">A factory that creates a new <typeparamref name="TContext" /> instance.</param>
    public LinqToDbRepository(IServiceProvider sp, Func<TContext> factory) : base(sp) {
        _factory = factory;
        _logger  = sp.GetService<ILogger<LinqToDbRepository<TContext, TEntity>>>();
        _context = factory();

        var entity = typeof(TEntity);

        TableName = entity.GetCustomAttribute<TableAttribute>(false)?.Name ?? entity.Name.Pluralize();
    }

    /// <summary>
    ///     The active LINQ to DB connection used by repository operations.
    /// </summary>
    protected virtual TContext Context => _context;

    /// <summary>
    ///     The table name used for CRUD operations, derived from <see cref="TableAttribute" /> or the pluralized entity name.
    /// </summary>
    public virtual string TableName { get; }

    protected override IQueryable<TEntity> AsQueryable() { return Context.GetTable<TEntity>().TableName(TableName).AsQueryable(); }

    public override async Task AddAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (!await RunAddAdvisorsAsync(entity, ct)) {
            return;
        }

        EnsureWriteUnitOfWork();

        await Context.InsertAsync(entity, TableName, token: ct);
    }

    /// <summary>
    ///     Runs the add-advisor chain per entity, then persists the survivors with a single
    ///     bulk-copy round trip instead of one insert per entity.
    /// </summary>
    public override async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var staged = new List<TEntity>();
        foreach (var entity in entities) {
            ct.ThrowIfCancellationRequested();
            if (await RunAddAdvisorsAsync(entity, ct)) {
                staged.Add(entity);
            }
        }

        if (staged.Count == 0) {
            return;
        }

        EnsureWriteUnitOfWork();

        await Context.GetTable<TEntity>().TableName(TableName).BulkCopyAsync(new BulkCopyOptions { BulkCopyType = BulkCopyType.ProviderSpecific }, staged, ct);
    }

    public override async Task UpdateAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        switch (await Advisor.For<IRepositoryUpdateAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, this, entity, ct)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return;
            case AdviseResult.Continue:
            default:
                break;
        }

        EnsureWriteUnitOfWork();

        if (IsConcurrencyControlled) {
            var rows = await Context.GetTable<TEntity>().TableName(TableName).UpdateOptimisticAsync(entity, ct);
            if (rows == 0) {
                throw new AbortedException();
            }
        } else {
            await Context.UpdateAsync(entity, TableName, token: ct);
        }

        TrackUpdate(entity);
    }

    public override async Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
        switch (await Advisor.For<IRepositoryRemoveAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, this, entity, ct)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return;
            case AdviseResult.Continue:
            default:
                break;
        }

        EnsureWriteUnitOfWork();

        TrackRemove(entity);

        await Context.DeleteAsync(entity, TableName, token: ct);
    }

    protected override ConfiguredCancelableAsyncEnumerable<TResult> AsAsyncEnumerable<TResult>(
        IQueryable<TResult> query,
        CancellationToken   ct
    ) {
        return query.AsAsyncEnumerable().WithCancellation(ct);
    }

    protected override Task<TResult?> FirstOrDefaultAsync<TResult>(IQueryable<TResult> query, CancellationToken ct)
        where TResult : default {
        return query.FirstOrDefaultAsync(ct);
    }

    protected override Task<TResult?> SingleOrDefaultAsync<TResult>(IQueryable<TResult> query, CancellationToken ct)
        where TResult : default {
        return query.SingleOrDefaultAsync(ct);
    }

    protected override Task<bool> AnyAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) {
        return query.AnyAsync(ct);
    }

    protected override Task<int> CountAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) {
        return query.CountAsync(ct);
    }

    protected override Task<long> LongCountAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) {
        return query.LongCountAsync(ct);
    }

    public override async ValueTask<long> EstimateCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        try {
            var query    = await BuildQueryAsync(predicate, ct);
            var provider = EstimateQueries.GetProvider(Context.DataProvider.Name);
            if (provider is EstimateProvider.None) {
                return await base.EstimateCountAsync(predicate, ct);
            }

            if ((provider is EstimateProvider.SqlServer or EstimateProvider.Sqlite) && EstimateQueries.HasWhere(query.Expression)) {
                return await base.EstimateCountAsync(predicate, ct);
            }

            var table = Context.GetTable<TEntity>();
            switch (provider) {
                case EstimateProvider.PostgreSql: {
                    var sql = query.ToSqlQuery(new() { InlineParameters = false });
                    var json = Context.Execute<string>("EXPLAIN (FORMAT JSON) " + sql.Sql, sql.Parameters.ToArray());
                    if (EstimateQueries.TryParsePostgreSql(json, out var rows)) return rows;
                    break;
                }
                case EstimateProvider.MySql: {
                    var sql = query.ToSqlQuery(new() { InlineParameters = false });
                    var json = Context.Execute<string>("EXPLAIN FORMAT=JSON " + sql.Sql, sql.Parameters.ToArray());
                    if (EstimateQueries.TryParseMySql(json, out var rows)) return rows;
                    break;
                }
                case EstimateProvider.SqlServer: {
                    var full = $"{table.SchemaName ?? "dbo"}.{table.TableName}";
                    var rows = Context.Execute<long>(
                        "SELECT COALESCE(SUM(p.[rows]), 0) FROM sys.partitions p WHERE p.object_id = OBJECT_ID(@full) AND p.index_id IN (0,1)",
                        new DataParameter("@full", full));
                    return rows;
                }
                case EstimateProvider.Sqlite: {
                    var rows = Context.Execute<long?>(
                        "SELECT MAX(stat) FROM sqlite_stat1 WHERE tbl = @t",
                        new DataParameter("@t", table.TableName));
                    if (rows is not null) return rows.Value;
                    break;
                }
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Count estimate failed; falling back to an exact count.");
        }

        return await base.EstimateCountAsync(predicate, ct);
    }

    protected override IUnitOfWork CreateUnitOfWork() {
        return new LinqToDbUnitOfWork<TContext>(_factory, ServiceProvider);
    }

    protected override void AttachContext(IUnitOfWork uow) {
        if (uow is not LinqToDbUnitOfWork<TContext> db) {
            throw new InvalidOperationException($"UoW of type {
                uow.GetType()
            } is not compatible with {
                typeof(TContext)
            }.");
        }

        _context.Dispose();

        _context = db.Context;
    }

    protected override void DisposeContext() { _context.Dispose(); }

    protected override ValueTask DisposeContextAsync() { return _context.DisposeAsync(); }
}
