using System;
using System.Linq;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core implementation of <see cref="RepositoryBase{TEntity}" />.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext" /> type.</typeparam>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public class EfCoreRepository<TContext, TEntity> : RepositoryBase<TEntity>, IQueryCacheKeyProvider
    where TContext : DbContext
    where TEntity : class
{
    private readonly IDbContextFactory<TContext> _factory;

    private          TContext                    _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreRepository{TContext,TEntity}" /> class.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="factory">The factory that creates a dedicated EF Core database context.</param>
    public EfCoreRepository(IServiceProvider sp, IDbContextFactory<TContext> factory) : base(sp) {
        _factory = factory;
        _context = factory.CreateDbContext();
    }

    /// <summary>
    ///     The active EF Core context used by repository operations.
    /// </summary>
    protected virtual TContext Context => _context;

    /// <summary>
    ///     The EF Core set for <typeparamref name="TEntity" /> on the active context.
    /// </summary>
    protected virtual DbSet<TEntity> DbSet => _context.Set<TEntity>();

    protected override IQueryable<TEntity> AsQueryable() { return DbSet.AsQueryable(); }

    public override async Task AddAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (!await RunAddAdvisorsAsync(entity, ct)) {
            return;
        }

        EnsureWriteUnitOfWork();

        await Context.AddAsync(entity, ct);
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

        var entry = Context.Entry(entity);

        if (entry.State == EntityState.Added) {
            return;
        }

        var staged = IsConcurrencyControlled && HasPendingUpdate(entity);

        TrackUpdate(entity);

        // First staging re-snapshots the tracker baseline from the incoming values, so a
        // caller-supplied stale token predicates the commit; restaging the same instance keeps the
        // tracker's original token, or the first staging's rotated value would race itself.
        if (!staged) {
            entry.State = EntityState.Detached;

            Context.Update(entity);
        }

        if (IsConcurrencyControlled) {
            entry.Property(nameof(IConcurrency.Timestamp)).CurrentValue = Guid.NewGuid();
        }
    }

    public override async Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

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

        Context.Remove(entity);
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
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        var estimator = ServiceProvider.GetService<IEfCoreCountEstimator<TContext>>();
        if (estimator is null) {
            return null;
        }

        var query = await BuildQueryAsync(predicate, ct);
        return await estimator.EstimateAsync(Context, query, ct);
    }

    /// <summary>
    ///     Builds a stable cache key for the query's translated SQL. Returns
    ///     <see langword="null" /> for non-relational providers and SQLite in-memory
    ///     connections, whose identity cannot distinguish databases.
    /// </summary>
    /// <param name="query">The translated query.</param>
    /// <typeparam name="TResult">The query result element type.</typeparam>
    /// <returns>The hashed cache key, or <see langword="null" /> when caching must be disabled.</returns>
    public string? GetQueryCacheKey<TResult>(IQueryable<TResult> query) {
        if (Context.Database.IsRelational() is false || Context.Database.ProviderName is not { } provider) {
            return null;
        }

        var connection = Context.Database.GetDbConnection();
        if (IsEphemeralDataSource(connection)) {
            return null;
        }

        using var command = query.CreateDbCommand();

        return QueryCacheKey.Create(
            provider,
            DescribeSource(connection),
            command.CommandText,
            command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, DescribeDbType(p), (object?)p.Value)));
    }

    // Full connection identity joins the hash: a redacted credential after the first open only
    // costs a cache miss, while a principal subset would collide across security contexts.
    private static string DescribeSource(DbConnection connection) {
        return Frame(connection.GetType().ToString())
             + Frame(connection.DataSource)
             + Frame(connection.Database)
             + Frame(connection.ConnectionString);
    }

    private static string Frame(string? value) {
        var content = value ?? string.Empty;

        return $"{Encoding.UTF8.GetByteCount(content)}:{content}";
    }

    private static bool IsEphemeralDataSource(DbConnection connection) {
        var dataSource = connection.DataSource;

        return string.IsNullOrEmpty(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || HasMemoryLifetime(connection.ConnectionString);
    }

    private static bool HasMemoryLifetime(string? connectionString) {
        if (string.IsNullOrEmpty(connectionString)) {
            return false;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        return builder.TryGetValue("Mode", out var mode)
            && mode is string lifetime
            && lifetime.Equals("Memory", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeDbType(DbParameter parameter) {
        var type = parameter.DbType.ToString();
        if (parameter.Size > 0) {
            type += $"({parameter.Size})";
        } else if (parameter.Precision > 0 || parameter.Scale > 0) {
            type += $"({parameter.Precision},{parameter.Scale})";
        }

        return type;
    }

    protected override IUnitOfWork CreateUnitOfWork() { return new EfCoreUnitOfWork<TContext>(_factory); }

    protected override void AttachContext(IUnitOfWork uow) {
        if (uow is not EfCoreUnitOfWork<TContext> ef) {
            throw new InvalidOperationException($"UoW of type {
                uow.GetType()
            } is not compatible with repository expecting {
                typeof(TContext)
            }.");
        }

        _context.Dispose();

        _context = ef.Context;
    }

    protected override void DisposeContext() { _context.Dispose(); }

    protected override ValueTask DisposeContextAsync() { return _context.DisposeAsync(); }
}
