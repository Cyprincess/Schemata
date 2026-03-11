using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.EntityFrameworkCore;

public class EntityFrameworkCoreRepository<TContext, TEntity> : RepositoryBase<TEntity>
    where TContext : DbContext
    where TEntity : class
{
    private readonly TContext _context;

    public EntityFrameworkCoreRepository(IServiceProvider sp, TContext context) : base(sp) { _context = context; }

    protected virtual TContext Context => _context;

    protected virtual DbSet<TEntity> DbSet => _context.Set<TEntity>();

    public override IAsyncEnumerable<TEntity> AsAsyncEnumerable() { return DbSet.AsAsyncEnumerable(); }

    public override IQueryable<TEntity> AsQueryable() { return DbSet.AsQueryable(); }

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

    public override IAsyncEnumerable<TResult> SearchAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        throw new NotImplementedException();
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
                return default;
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
                return default;
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
                return default;
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
                return default;
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
                return default;
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
                return default;
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

        Detach(entity);

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

        Context.Remove(entity);
    }

    public override async ValueTask<int> CommitAsync(CancellationToken ct = default) {
        return await Context.SaveChangesAsync(ct);
    }

    public override void Detach(TEntity entity) { Context.Entry(entity).State = EntityState.Detached; }

    private async Task<IQueryable<TResult>> BuildQueryAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct
    ) {
        ct.ThrowIfCancellationRequested();

        var container = AsQueryContainer();

        switch (await Advisor.For<IRepositoryBuildQueryAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, container, ct)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return BuildQuery(container.Query, predicate);
    }
}
