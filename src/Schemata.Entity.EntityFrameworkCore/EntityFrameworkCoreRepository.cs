using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.EntityFrameworkCore;

/// <summary>
///     Entity Framework Core implementation of <see cref="RepositoryBase{TEntity}" />.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext" /> type.</typeparam>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public class EntityFrameworkCoreRepository<TContext, TEntity> : RepositoryBase<TEntity>
    where TContext : DbContext
    where TEntity : class
{
    private TContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EntityFrameworkCoreRepository{TContext, TEntity}" /> class.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="factory">The factory used to create a dedicated EF Core database context.</param>
    public EntityFrameworkCoreRepository(IServiceProvider sp, IDbContextFactory<TContext> factory) : base(sp) {
        _context = factory.CreateDbContext();
    }

    protected virtual TContext Context => _context;

    protected virtual DbSet<TEntity> DbSet => _context.Set<TEntity>();

    protected override IQueryable<TEntity> AsQueryable() { return DbSet.AsQueryable(); }

    public override async IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        [EnumeratorCancellation] CancellationToken      ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var enumerable = query.AsAsyncEnumerable().WithCancellation(ct);

        await foreach (var entity in enumerable) {
            ct.ThrowIfCancellationRequested();
            yield return entity;
        }
    }

    public override async ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    )
        where TResult : default {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, TResult>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, TResult>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return default;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await query.FirstOrDefaultAsync(ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, TResult>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return default;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public override async ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    )
        where TResult : default {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, TResult>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, TResult>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return default;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await query.SingleOrDefaultAsync(ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, TResult>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return default;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public override async ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, bool>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, bool>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return false;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await query.AnyAsync(ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, bool>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return false;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public override async ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, int>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, int>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return 0;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await query.CountAsync(ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, int>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return 0;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public override async ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, long>(this, query);

        switch (await Advisor.For<IRepositoryQueryAdvisor<TEntity, TResult, long>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return 0;
            case AdviseResult.Handle:
                return context.Result;
            case AdviseResult.Continue:
            default:
                break;
        }

        context.Result = await query.LongCountAsync(ct);

        switch (await Advisor.For<IRepositoryResultAdvisor<TEntity, TResult, long>>()
                             .RunAsync(AdviceContext, context, ct)) {
            case AdviseResult.Block:
                return 0;
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return context.Result;
    }

    public override async Task AddAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        switch (await Advisor.For<IRepositoryAddAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, this, entity, ct)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return;
            case AdviseResult.Continue:
            default:
                break;
        }

        TrackAdd(entity);

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

        TrackUpdate(entity);

        Context.Entry(entity).State = EntityState.Detached;

        Context.Update(entity);
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

        TrackRemove(entity);

        Context.Remove(entity);
    }

    public override IUnitOfWork Begin() {
        var uow = ServiceProvider.GetRequiredService<IUnitOfWork<TContext>>();

        Join(uow);

        return uow;
    }

    public override async Task CommitAsync(CancellationToken ct = default) {
        if (!OwnsContext) {
            throw new InvalidOperationException(
                "Repository is enlisted in a unit of work. Call IUnitOfWork.CommitAsync instead.");
        }

        await _context.SaveChangesAsync(ct);
        var snapshot = SnapshotChanges();
        await DispatchCommittedAsync(snapshot, ct);
    }

    protected override void AttachContext(IUnitOfWork uow) {
        if (uow is not EfCoreUnitOfWork<TContext> ef) {
            throw new InvalidOperationException($"UoW of type {
                uow.GetType()
            } is not compatible with repository expecting {
                typeof(TContext)
            }.");
        }

        // base.Join has already verified _added/_updated/_removed are empty before calling
        // this; nothing to roll back on this side.
        _context.Dispose();
        _context = ef.Context;

        ef.AddCommitSink(async ct => { await DispatchCommittedAsync(SnapshotChanges(), ct); });

        ef.AddRollbackSink(ResetTracking);
    }

    protected override void DisposeContext() { _context.Dispose(); }

    protected override ValueTask DisposeContextAsync() { return _context.DisposeAsync(); }

    private async Task<IQueryable<TResult>> BuildQueryAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct
    ) {
        ct.ThrowIfCancellationRequested();

        var container = AsQueryContainer();

        switch (await Advisor.For<IRepositoryBuildQueryAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, container, ct)) {
            case AdviseResult.Continue:
            case AdviseResult.Handle:
            default:
                break;
            case AdviseResult.Block:
                container = AsQueryContainer();
                container.ApplyModification(q => q.Where(_ => false));
                break;
        }

        return BuildQuery(container.Query, predicate);
    }
}
