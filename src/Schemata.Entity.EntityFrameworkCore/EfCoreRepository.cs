using System;
using System.Linq;
using System.Linq.Expressions;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
public class EfCoreRepository<TContext, TEntity> : RepositoryBase<TEntity>, IQueryCacheKeyProvider, IRepositorySource
    where TContext : DbContext
    where TEntity : class
{
    private readonly IDbContextFactory<TContext> _factory;
    private readonly RepositoryBinding           _binding;

    private          TContext                    _context;
    private EfCoreUnitOfWork<TContext>? _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreRepository{TContext,TEntity}" /> class.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="factory">The factory that creates a dedicated EF Core database context.</param>
    public EfCoreRepository(IServiceProvider sp, IDbContextFactory<TContext> factory) : base(sp) {
        _factory = factory;
        _binding = sp.GetService<RepositoryBinding>() ?? RepositoryBinding.Default;
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

    public override async Task<MutationResult> AddAsync(TEntity entity, CancellationToken ct = default, MutationOptions options = default) {
        ct.ThrowIfCancellationRequested();

        var mark   = WriteSequence;
        var advise = await RunAddAdvisorsAsync(entity, options, ct);
        if (advise != AdviseResult.Continue) {
            return ResolveWriteResult(mark, advise);
        }

        EnsureWriteUnitOfWork();

        var entry = await Context.AddAsync(entity, ct);

        StageWrite();
        _unitOfWork!.Admit(entry);

        return MutationResult.Applied;
    }

    public override async ValueTask<OriginalResult<TResult>> GetOriginalAsync<TResult>(
        TEntity entity,
        Expression<Func<TEntity, TResult>> projection,
        CancellationToken ct = default,
        QueryOptions options = default
    ) {
        ct.ThrowIfCancellationRequested();
        TrackEvictionSuppression(options.IgnoreCacheEviction);
        ValidateOriginalProjection(projection);
        var admitted = _unitOfWork?.FindAdmission(entity);
        if (admitted is null) return await base.GetOriginalAsync(entity, projection, ct, options);

        var container = new QueryContainer<TEntity>(this, admitted, options);
        var keys = OriginalKeyPredicate(entity);
        var query = await BuildQueryAsync(container, q => q.Where(keys).Select(projection)
            .Select(value => new OriginalResult<TResult>(true, value)), ct, options);
        return query.SingleOrDefault();
    }

    public override async Task<MutationResult> UpdateAsync(TEntity entity, CancellationToken ct = default, MutationOptions options = default) {
        ct.ThrowIfCancellationRequested();
        TrackEvictionSuppression(options.IgnoreCacheEviction);

        var mark = WriteSequence;

        var advise = await Advisor.For<IRepositoryUpdateAdvisor<TEntity>>()
                                  .RunAsync(AdviceContext, this, entity, options, ct);
        if (advise != AdviseResult.Continue) {
            return ResolveWriteResult(mark, advise);
        }

        EnsureWriteUnitOfWork();

        var entry = Context.Entry(entity);

        if (entry.State == EntityState.Added) {
            StageWrite();

            return MutationResult.Applied;
        }

        // The caller's stamp predicates the commit; the unit of work rotates it at save time.
        // Detached instances attach with the caller values (the stamp becomes the original value);
        // tracked instances keep their tracker state — only the concurrency baseline is re-read from
        // the caller — and are forced to a full update so a touch still writes.
        if (entry.State == EntityState.Detached) {
            Context.Update(entity);
        } else {
            if (IsConcurrencyControlled) {
                entry.Property(nameof(IConcurrency.Timestamp)).OriginalValue = ((IConcurrency)entity).Timestamp;
            }

            entry.State = EntityState.Modified;
        }

        StageWrite();

        return MutationResult.Applied;
    }

    public override async Task<MutationResult> RemoveAsync(TEntity entity, CancellationToken ct = default, MutationOptions options = default) {
        ct.ThrowIfCancellationRequested();
        TrackEvictionSuppression(options.IgnoreCacheEviction);

        var mark = WriteSequence;

        var advise = await Advisor.For<IRepositoryRemoveAdvisor<TEntity>>()
                                  .RunAsync(AdviceContext, this, entity, options, ct);
        if (advise != AdviseResult.Continue) {
            return ResolveWriteResult(mark, advise);
        }

        EnsureWriteUnitOfWork();

        Context.Remove(entity);

        StageWrite();

        return MutationResult.Applied;
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
        CancellationToken ct = default,
        QueryOptions options = default
    ) {
        ct.ThrowIfCancellationRequested();
        TrackEvictionSuppression(options.IgnoreCacheEviction);
        var estimator = ServiceProvider.GetService<IEfCoreCountEstimator<TContext>>();
        if (estimator is null) {
            return null;
        }

        var query = await BuildQueryAsync(predicate, ct, options);
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
        if (EfCoreSourceKeys.IsEphemeralDataSource(connection)) {
            return null;
        }

        using var command = query.CreateDbCommand();

        return QueryCacheKey.Create(
            provider,
            EfCoreSourceKeys.DescribeSource(connection),
            command.CommandText,
            command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, DescribeDbType(p), (object?)p.Value)));
    }

    /// <inheritdoc />
    public string SourceKey { get { return EfCoreSourceKeys.CreateKey(_context, _factory, _binding); } }

    private static string DescribeDbType(DbParameter parameter) {
        var type = parameter.DbType.ToString();
        if (parameter.Size > 0) {
            type += $"({parameter.Size})";
        } else if (parameter.Precision > 0 || parameter.Scale > 0) {
            type += $"({parameter.Precision},{parameter.Scale})";
        }

        return type;
    }

    protected override IUnitOfWork CreateUnitOfWork() { return new EfCoreUnitOfWork<TContext>(_factory, ServiceProvider); }

    protected override void AttachContext(IUnitOfWork uow) {
        if (uow is not EfCoreUnitOfWork<TContext> ef) {
            throw new InvalidOperationException($"UoW of type {
                uow.GetType()
            } is not compatible with repository expecting {
                typeof(TContext)
            }.");
        }

        // The unit of work's source resolution may create its context lazily; a creation failure
        // propagates from its own compensation path and leaves the owned context usable. The owned
        // context is released only after type, source, and binding all match.
        if (ef.SourceKey != SourceKey) {
            throw new InvalidOperationException(
                $"Cannot join a unit of work bound to a different source or tenant binding; repository context type {typeof(TContext)}.");
        }

        _context.Dispose();

        _context = ef.Context;
        _unitOfWork = ef;
    }

    protected override void DisposeContext() { _context.Dispose(); }

    protected override ValueTask DisposeContextAsync() { return _context.DisposeAsync(); }
}
