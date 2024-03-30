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

public class EntityFrameworkCoreRepository<TContext, TEntity> : RepositoryBase<TEntity>
    where TContext : DbContext
    where TEntity : class
{
    public EntityFrameworkCoreRepository(IServiceProvider sp, TContext context) {
        ServiceProvider = sp;
        Context         = context;
    }

    protected TContext Context { get; }

    protected IServiceProvider ServiceProvider { get; }

    public override async IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        [EnumeratorCancellation] CancellationToken      ct = default) {
        var query      = await BuildQueryAsync(predicate, ct);
        var enumerable = query.AsAsyncEnumerable().WithCancellation(ct);

        await foreach (var entity in enumerable) {
            ct.ThrowIfCancellationRequested();
            yield return entity;
        }
    }

    public override async Task<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default)
        where TResult : default {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.FirstOrDefaultAsync(ct);
    }

    public override async Task<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default)
        where TResult : default {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.SingleOrDefaultAsync(ct);
    }

    public override async Task<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.AnyAsync(ct);
    }

    public override async Task<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.CountAsync(ct);
    }

    public override async Task<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.LongCountAsync(ct);
    }

    public override async Task AddAsync(TEntity entity, CancellationToken ct = default) {
        var next = await Advices<IRepositoryAddAsyncAdvice<TEntity>>.AdviseAsync(ServiceProvider, entity, ct);
        if (!next) return;

        await Context.AddAsync(entity, ct);
    }

    public override async Task UpdateAsync(TEntity entity, CancellationToken ct = default) {
        var next = await Advices<IRepositoryUpdateAsyncAdvice<TEntity>>.AdviseAsync(ServiceProvider, entity, ct);
        if (!next) return;

        Context.Entry(entity).State = EntityState.Detached;
        Context.Update(entity);
    }

    public override async Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
        var next = await Advices<IRepositoryRemoveAsyncAdvice<TEntity>>.AdviseAsync(ServiceProvider, entity, ct);
        if (!next) return;

        Context.Remove(entity);
    }

    public override async Task<int> CommitAsync(CancellationToken ct = default) {
        return await Context.SaveChangesAsync(ct);
    }

    private async Task<IQueryable<TResult>> BuildQueryAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct) {
        var table = Context.Set<TEntity>().AsQueryable();

        var query = new QueryContainer<TEntity>(table);

        await Advices<IRepositoryQueryAsyncAdvice<TEntity>>.AdviseAsync(ServiceProvider, query, ct);

        return BuildQuery(query.Query, predicate);
    }
}
