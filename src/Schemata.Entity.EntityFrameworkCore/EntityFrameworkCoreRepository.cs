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
    public EntityFrameworkCoreRepository(TContext context, IServiceProvider provider) {
        Context  = context;
        Provider = provider;
    }

    protected TContext Context { get; }

    protected IServiceProvider Provider { get; }

    public override async IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        [EnumeratorCancellation] CancellationToken      ct = default) {
        var enumerable = BuildQuery(predicate).AsAsyncEnumerable().WithCancellation(ct);

        await foreach (var entity in enumerable) {
            ct.ThrowIfCancellationRequested();
            yield return entity;
        }
    }

    public override async Task<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default)
        where TResult : default {
        return await BuildQuery(predicate).FirstOrDefaultAsync(ct);
    }

    public override async Task<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default)
        where TResult : default {
        return await BuildQuery(predicate).SingleOrDefaultAsync(ct);
    }

    public override async Task<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        return await BuildQuery(predicate).AnyAsync(ct);
    }

    public override async Task<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        return await BuildQuery(predicate).CountAsync(ct);
    }

    public override async Task<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        return await BuildQuery(predicate).LongCountAsync(ct);
    }

    public override async Task AddAsync(TEntity entity, CancellationToken ct = default) {
        await Advices<IRepositoryAddAsyncAdvice<TEntity>>.AdviseAsync(Provider, entity, ct);

        await Context.AddAsync(entity, ct);
    }

    public override async Task UpdateAsync(TEntity entity, CancellationToken ct = default) {
        await Advices<IRepositoryUpdateAsyncAdvice<TEntity>>.AdviseAsync(Provider, entity, ct);

        Context.Entry(entity).State = EntityState.Detached;
        Context.Update(entity);
    }

    public override async Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
        await Advices<IRepositoryRemoveAsyncAdvice<TEntity>>.AdviseAsync(Provider, entity, ct);

        Context.Remove(entity);
    }

    public override async Task<int> CommitAsync(CancellationToken ct = default) {
        return await Context.SaveChangesAsync(ct);
    }

    private IQueryable<TResult> BuildQuery<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate) {
        var table = Context.Set<TEntity>().AsQueryable();
        return BuildQuery(table, predicate);
    }
}
