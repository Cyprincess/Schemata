using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advices;

namespace Schemata.Entity.EntityFrameworkCore;

public class EntityFrameworkCoreRepository<TContext, TEntity> : RepositoryBase<TEntity> where TContext : DbContext
                                                                                        where TEntity : class
{
    private readonly TContext _context;

    public EntityFrameworkCoreRepository(IServiceProvider sp, TContext context) : base(sp) {
        _context = context;
    }

    protected virtual TContext Context => _context;

    protected virtual DbSet<TEntity> DbSet => _context.Set<TEntity>();

    public override IAsyncEnumerable<TEntity> AsAsyncEnumerable() {
        return DbSet.AsAsyncEnumerable();
    }

    public override IQueryable<TEntity> AsQueryable() {
        return DbSet.AsQueryable();
    }

    public override async IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        [EnumeratorCancellation] CancellationToken      ct = default) {
        var query = await BuildQueryAsync(predicate, ct);

        var enumerable = query.AsAsyncEnumerable().WithCancellation(ct);

        await foreach (var entity in enumerable) {
            ct.ThrowIfCancellationRequested();
            yield return entity;
        }
    }

    public override IAsyncEnumerable<TResult> SearchAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        throw new NotImplementedException();
    }

    public override async ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) where TResult : default {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, TResult>(this, query);
        var next = await Advices<IRepositoryQueryAsyncAdvice<TEntity, TResult, TResult>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        if (!next) {
            return context.Result;
        }

        context.Result = await query.FirstOrDefaultAsync(ct);
        await Advices<IRepositoryResultAdvice<TEntity, TResult, TResult>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        return context.Result;
    }

    public override async ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) where TResult : default {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, TResult>(this, query);
        var next = await Advices<IRepositoryQueryAsyncAdvice<TEntity, TResult, TResult>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        if (!next) {
            return context.Result;
        }

        context.Result = await query.SingleOrDefaultAsync(ct);
        await Advices<IRepositoryResultAdvice<TEntity, TResult, TResult>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        return context.Result;
    }

    public override async ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, bool>(this, query);
        var next = await Advices<IRepositoryQueryAsyncAdvice<TEntity, TResult, bool>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        if (!next) {
            return context.Result;
        }

        context.Result = await query.AnyAsync(ct);
        await Advices<IRepositoryResultAdvice<TEntity, TResult, bool>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        return context.Result;
    }

    public override async ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, int>(this, query);
        var next = await Advices<IRepositoryQueryAsyncAdvice<TEntity, TResult, int>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        if (!next) {
            return context.Result;
        }

        context.Result = await query.CountAsync(ct);
        await Advices<IRepositoryResultAdvice<TEntity, TResult, int>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        return context.Result;
    }

    public override async ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);

        var context = new QueryContext<TEntity, TResult, long>(this, query);
        var next = await Advices<IRepositoryQueryAsyncAdvice<TEntity, TResult, long>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        if (!next) {
            return context.Result;
        }

        context.Result = await query.LongCountAsync(ct);
        await Advices<IRepositoryResultAdvice<TEntity, TResult, long>>.AdviseAsync(ServiceProvider, AdviceContext, context, ct);
        return context.Result;
    }

    public override async Task AddAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var next = await Advices<IRepositoryAddAsyncAdvice<TEntity>>.AdviseAsync(ServiceProvider, AdviceContext, this, entity, ct);
        if (!next) {
            return;
        }

        await Context.AddAsync(entity, ct);
    }

    public override async Task UpdateAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var next = await Advices<IRepositoryUpdateAsyncAdvice<TEntity>>.AdviseAsync(ServiceProvider, AdviceContext, this, entity, ct);
        if (!next) {
            return;
        }

        Detach(entity);

        Context.Update(entity);
    }

    public override async Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var next = await Advices<IRepositoryRemoveAsyncAdvice<TEntity>>.AdviseAsync(ServiceProvider, AdviceContext, this, entity, ct);
        if (!next) {
            return;
        }

        Context.Remove(entity);
    }

    public override async ValueTask<int> CommitAsync(CancellationToken ct = default) {
        return await Context.SaveChangesAsync(ct);
    }

    public override void Detach(TEntity entity) {
        Context.Entry(entity).State = EntityState.Detached;
    }

    private async Task<IQueryable<TResult>> BuildQueryAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct) {
        ct.ThrowIfCancellationRequested();

        var container = AsQueryContainer();

        await Advices<IRepositoryBuildQueryAdvice<TEntity>>.AdviseAsync(ServiceProvider, AdviceContext, container, ct);

        return BuildQuery(container.Query, predicate);
    }
}
