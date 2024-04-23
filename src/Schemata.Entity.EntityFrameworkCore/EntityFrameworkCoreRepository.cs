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
    public EntityFrameworkCoreRepository(IServiceProvider sp, TContext context) : base(sp) {
        Context = context;
        DbSet   = context.Set<TEntity>();
    }

    protected virtual TContext Context { get; }

    protected virtual DbSet<TEntity> DbSet { get; }

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
        CancellationToken                               ct = default)
        where TResult : default {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.FirstOrDefaultAsync(ct);
    }

    public override async ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default)
        where TResult : default {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.SingleOrDefaultAsync(ct);
    }

    public override async ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.AnyAsync(ct);
    }

    public override async ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.CountAsync(ct);
    }

    public override async ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default) {
        var query = await BuildQueryAsync(predicate, ct);
        return await query.LongCountAsync(ct);
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

        Context.Entry(entity).State = EntityState.Detached;
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

    private async Task<IQueryable<TResult>> BuildQueryAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct) {
        ct.ThrowIfCancellationRequested();

        var table = AsQueryable();

        var query = new QueryContainer<TEntity>(this, table);

        await Advices<IRepositoryBuildQueryAdvice<TEntity>>.AdviseAsync(ServiceProvider, AdviceContext, query, ct);

        return BuildQuery(query.Query, predicate);
    }
}
