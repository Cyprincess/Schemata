using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

public class LinQ2DbRepository<TContext, TEntity> : RepositoryBase<TEntity>
    where TContext : DataConnection
    where TEntity : class
{
    public LinQ2DbRepository(TContext context, IServiceProvider provider) {
        Context  = context;
        Provider = provider;
    }

    protected TContext         Context  { get; }
    protected IServiceProvider Provider { get; }

    protected DataConnectionTransaction? Transaction  { get; set; }
    protected int                        RowsAffected { get; set; }

    public string TableName { get; } = typeof(TEntity).Name.Pluralize();

    public override async IAsyncEnumerable<TEntity> ListAsync(
        Expression<Func<TEntity, bool>>?           predicate,
        [EnumeratorCancellation] CancellationToken ct = default) {
        var enumerable = BuildQuery(predicate).AsAsyncEnumerable().WithCancellation(ct);

        await foreach (var entity in enumerable) yield return entity;
    }

    public override async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default) {
        return await BuildQuery(predicate).FirstOrDefaultAsync(ct);
    }

    public override async Task<TEntity?> SingleOrDefaultAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default) {
        return await BuildQuery(predicate).SingleOrDefaultAsync(ct);
    }

    public override async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default) {
        return await BuildQuery(predicate).AnyAsync(ct);
    }

    public override async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default) {
        return await BuildQuery(predicate).CountAsync(ct);
    }

    public override async Task<long> LongCountAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default) {
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

    private IQueryable<TEntity> BuildQuery(Expression<Func<TEntity, bool>>? predicate) {
        var table = Context.GetTable<TEntity>().TableName(TableName);

        return predicate != null ? table.Where(predicate) : table;
    }

    private async Task BeginTransactionAsync(CancellationToken ct) {
        if (Transaction != null) return;

        Transaction = await Context.BeginTransactionAsync(ct);
    }
}
