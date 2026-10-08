using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Concurrency;
using LinqToDB.Data;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Estimation;

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
public class LinqToDbRepository<TContext, TEntity> : RepositoryBase<TEntity>, IQueryCacheKeyProvider
    where TContext : DataConnection
    where TEntity : class
{
    private readonly Func<TContext>   _factory;
    private          TContext         _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LinqToDbRepository{TContext,TEntity}" /> class.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="factory">A factory that creates a new <typeparamref name="TContext" /> instance.</param>
    public LinqToDbRepository(IServiceProvider sp, Func<TContext> factory) : base(sp) {
        _factory = factory;
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

    public override async Task<MutationResult> AddAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var mark   = WriteSequence;
        var advise = await RunAddAdvisorsAsync(entity, ct);
        if (advise != AdviseResult.Continue) {
            return ResolveWriteResult(mark, advise);
        }

        EnsureWriteUnitOfWork();

        try {
            await Context.InsertAsync(entity, TableName, token: ct);
        } catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex)) {
            // The SQL executes here, before any unit-of-work commit, so the provider's unique
            // violation must be classified at this actual execution boundary.
            throw CreateAlreadyExists(ex, entity);
        }

        StageWrite();

        return MutationResult.Applied;
    }

    /// <summary>
    ///     Runs the add-advisor chain per entity, then persists the survivors with a single
    ///     bulk-copy round trip instead of one insert per entity.
    /// </summary>
    public override async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var staged = new List<TEntity>();
        foreach (var entity in entities) {
            ct.ThrowIfCancellationRequested();
            if (await RunAddAdvisorsAsync(entity, ct) == AdviseResult.Continue) {
                staged.Add(entity);
            }
        }

        if (staged.Count == 0) {
            return;
        }

        EnsureWriteUnitOfWork();

        try {
            await Context.GetTable<TEntity>().TableName(TableName).BulkCopyAsync(new BulkCopyOptions { BulkCopyType = BulkCopyType.ProviderSpecific }, staged, ct);
        } catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex)) {
            // A batch cannot identify which row collided, so the error names the resource type
            // without guessing a conflicting resource name.
            throw CreateAlreadyExists(ex, entity: null);
        }

        // One type-level write fact per successful bulk copy; no entity journal is kept.
        StageWrite();
    }

    public override async Task<MutationResult> UpdateAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var mark = WriteSequence;

        var advise = await Advisor.For<IRepositoryUpdateAdvisor<TEntity>>()
                                  .RunAsync(AdviceContext, this, entity, ct);
        if (advise != AdviseResult.Continue) {
            return ResolveWriteResult(mark, advise);
        }

        EnsureWriteUnitOfWork();

        if (IsConcurrencyControlled) {
            int rows;
            try {
                rows = await Context.GetTable<TEntity>().TableName(TableName).UpdateOptimisticWithRefreshAsync(entity, ct);
            } catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex)) {
                // The optimistic UPDATE executes here, before any unit-of-work commit: a
                // matching version that moves a column onto an existing unique value fails at
                // this boundary and becomes ALREADY_EXISTS, while a stale version still fails
                // the zero-row check below as ABORTED.
                throw CreateAlreadyExists(ex, entity);
            }

            if (rows == 0) {
                throw SchemataResourceErrors.Aborted<TEntity>((entity as ICanonicalName)?.CanonicalName);
            }
        } else {
            try {
                await Context.UpdateAsync(entity, TableName, token: ct);
            } catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex)) {
                // An update that moves a column onto an existing unique value fails at execution
                // time, before the unit-of-work commit classifier could see it.
                throw CreateAlreadyExists(ex, entity);
            }
        }

        StageWrite();

        return MutationResult.Applied;
    }

    public override async Task<MutationResult> RemoveAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var mark = WriteSequence;

        var advise = await Advisor.For<IRepositoryRemoveAdvisor<TEntity>>()
                                  .RunAsync(AdviceContext, this, entity, ct);
        if (advise != AdviseResult.Continue) {
            return ResolveWriteResult(mark, advise);
        }

        EnsureWriteUnitOfWork();

        if (IsConcurrencyControlled) {
            var rows = await Context.GetTable<TEntity>().TableName(TableName).DeleteOptimisticAsync(entity, ct);
            if (rows == 0) {
                throw SchemataResourceErrors.Aborted<TEntity>((entity as ICanonicalName)?.CanonicalName);
            }
        } else {
            await Context.DeleteAsync(entity, TableName, token: ct);
        }

        StageWrite();

        return MutationResult.Applied;
    }

    /// <summary>
    ///     Builds the consumer-facing ALREADY_EXISTS error for a unique-constraint violation raised
    ///     at the actual SQL execution boundary. The provider exception never crosses the wire; the
    ///     error carries the entity type and, when the colliding row is identifiable, its canonical
    ///     resource name.
    /// </summary>
    private static AlreadyExistsException CreateAlreadyExists(Exception exception, TEntity? entity) {
        return SchemataResourceErrors.AlreadyExists(typeof(TEntity), (entity as ICanonicalName)?.CanonicalName, innerException: exception);
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

    public override async ValueTask<long?> EstimateCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        var provider = EstimateQueries.GetProvider(Context.DataProvider.Name);
        if (provider is EstimateProvider.None) return null;
        var query = await BuildQueryAsync(predicate, ct);

        if (provider is EstimateProvider.Sqlite) {
            if (!EstimateQueries.IsTableRoot<TEntity>(query.Expression, TableName)) return null;
            var mapping = Context.MappingSchema.GetEntityDescriptor(typeof(TEntity), Context.Options.ConnectionOptions.OnEntityDescriptorCreated);
            if (mapping.QueryFilterLambda is not null || mapping.QueryFilterFunc is not null
             || mapping.InheritanceMapping.Count != 0 || mapping.InheritanceRoot is not null
             || mapping.HasCalculatedMembers || mapping.SchemaName is not null || mapping.DatabaseName is not null) return null;
            var exists = await Context.ExecuteAsync<long>(
                "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'sqlite_stat1')", ct);
            if (exists == 0) return null;
            var stat = await Context.ExecuteAsync<string?>(
                "SELECT stat FROM sqlite_stat1 WHERE tbl = @t AND (idx IS NULL OR idx IN "
                + "(SELECT name FROM pragma_index_list(@t) WHERE partial = 0)) ORDER BY idx LIMIT 1", ct, new DataParameter("@t", TableName));
            if (stat is null) return null;
            var end = stat.IndexOf(' ');
            var cardinality = end < 0 ? stat.AsSpan() : stat.AsSpan(0, end);
            if (!long.TryParse(cardinality, NumberStyles.None, CultureInfo.InvariantCulture, out var rows)) {
                throw new FormatException("Invalid SQLite table cardinality statistic.");
            }
            return rows;
        }

        if (provider is EstimateProvider.MySql && EstimateQueries.HasMySqlUnsupportedShape(query.Expression)) return null;
        var sql = query.ToSqlQuery(new() { InlineParameters = false });
        var parameters = sql.Parameters.ToArray();
        switch (provider) {
            case EstimateProvider.PostgreSql:
                return QueryPlanEstimate.Parse(await Context.ExecuteAsync<string>("EXPLAIN (FORMAT JSON) " + sql.Sql, ct, parameters),
                    QueryEstimateProvider.PostgreSql);
            case EstimateProvider.MySql:
                return QueryPlanEstimate.Parse(await Context.ExecuteAsync<string>("EXPLAIN FORMAT=JSON " + sql.Sql, ct, parameters),
                    QueryEstimateProvider.MySql);
            case EstimateProvider.SqlServer:
                if (((IDataContext)Context).CloseAfterUse) return null;
                var connection = await Context.OpenDbConnectionAsync(ct);
                return await QueryPlanEstimate.EstimateSqlServerAsync(connection, Context.CommandTimeout,
                    token => Context.ExecuteAsync<string>(sql.Sql, token, parameters),
                    async (text, timeout, token) => {
                        await using var mode = connection.CreateCommand();
                        mode.Transaction = Context.Transaction;
                        if (timeout >= 0) mode.CommandTimeout = timeout;
                        mode.CommandText = text;
                        await mode.ExecuteNonQueryAsync(token);
                    }, ct);
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    /// <summary>
    ///     Builds a stable cache key for the query's translated SQL. Returns
    ///     <see langword="null" /> for SQLite in-memory connections, whose connection string
    ///     cannot identify a specific database across scopes.
    /// </summary>
    /// <param name="query">The translated query.</param>
    /// <typeparam name="TResult">The query result element type.</typeparam>
    /// <returns>The hashed cache key, or <see langword="null" /> when caching must be disabled.</returns>
    public string? GetQueryCacheKey<TResult>(IQueryable<TResult> query) {
        var source = Context.ConnectionString;
        if (string.IsNullOrEmpty(source)
         || (Context.DataProvider.Name.StartsWith("SQLite", StringComparison.OrdinalIgnoreCase) && IsEphemeralDataSource(source))) {
            return null;
        }

        var sql = query.ToSqlQuery(new() { InlineParameters = false });

        return QueryCacheKey.Create(
            Context.DataProvider.Name,
            source,
            sql.Sql,
            sql.Parameters.Select(p => (p.Name ?? string.Empty, $"{p.DataType}:{p.DbType}", (object?)p.Value)));
    }

    // SQLite named in-memory lifetimes (Mode=Memory, Data Source=:memory:) reuse one connection
    // string for distinct databases, so their identity must not key a cross-scope cache.
    private static bool IsEphemeralDataSource(string? source) {
        if (string.IsNullOrEmpty(source)) {
            return true;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = source };

        if (builder.TryGetValue("Mode", out var mode)
         && mode is string lifetime
         && lifetime.Equals("Memory", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (!builder.TryGetValue("Data Source", out var database)
         && !builder.TryGetValue("DataSource", out database)
         && !builder.TryGetValue("Filename", out database)) {
            return true;
        }

        return database is not string path || path.Length == 0 || path.Equals(":memory:", StringComparison.OrdinalIgnoreCase);
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
