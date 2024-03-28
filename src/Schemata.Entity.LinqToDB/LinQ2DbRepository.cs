using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Humanizer;
using LinqToDB;
using LinqToDB.Data;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advices;

namespace Schemata.Entity.LinqToDB;

public class LinQ2DbRepository<TContext, TEntity>(TContext context, IServiceProvider provider) : RepositoryBase<TEntity>
    where TContext : DataConnection
    where TEntity : class
{
    protected TContext Context { get; } = context;

    protected IServiceProvider Provider { get; } = provider;

    protected DataConnectionTransaction? Transaction { get; set; }

    protected int RowsAffected { get; set; }

    public string TableName { get; } = typeof(TEntity).Name.Pluralize();

    public override async IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        [EnumeratorCancellation] CancellationToken      ct = default) {
        var enumerable = BuildQuery(predicate).AsAsyncEnumerable().WithCancellation(ct);

        await foreach (var entity in enumerable) yield return entity;
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

        await BeginTransactionAsync(ct);

        RowsAffected += await Context.InsertAsync(entity, TableName, token: ct);
    }

    public override async Task UpdateAsync(TEntity entity, CancellationToken ct = default) {
        await Advices<IRepositoryUpdateAsyncAdvice<TEntity>>.AdviseAsync(Provider, entity, ct);

        await BeginTransactionAsync(ct);

        RowsAffected += await Context.UpdateAsync(entity, TableName, token: ct);
    }

    public override async Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
        await Advices<IRepositoryRemoveAsyncAdvice<TEntity>>.AdviseAsync(Provider, entity, ct);

        await BeginTransactionAsync(ct);

        RowsAffected += await Context.DeleteAsync(entity, TableName, token: ct);
    }

    public override async Task<int> CommitAsync(CancellationToken ct = default) {
        if (Transaction == null) return 0;

        try {
            await Transaction.CommitAsync(ct);
        } catch (Exception ex) {
            await Transaction.RollbackAsync(ct);

            throw new TransactionAbortedException(ex.Message, ex);
        }

        var rows = RowsAffected;

        Transaction  = null;
        RowsAffected = 0;

        return rows;
    }

    private IQueryable<TResult> BuildQuery<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate) {
        var table = Context.GetTable<TEntity>().TableName(TableName).AsQueryable();
        return BuildQuery(table, predicate);
    }

    private async Task BeginTransactionAsync(CancellationToken ct) {
        if (Transaction != null) return;

        Transaction = await Context.BeginTransactionAsync(ct);
    }
}
