using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     LINQ to DB implementation of <see cref="RepositoryBase{TEntity}" /> with deferred execution
///     when no unit of work is active, aligning with Entity Framework Core semantics.
/// </summary>
/// <typeparam name="TContext">The <see cref="DataConnection" /> type.</typeparam>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public class LinQ2DbRepository<TContext, TEntity> : RepositoryBase<TEntity>
    where TContext : DataConnection
    where TEntity : class
{
    private readonly List<PendingOperation> _pending = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="LinQ2DbRepository{TContext, TEntity}" /> class.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="context">The LINQ to DB data connection.</param>
    /// <param name="uow">An optional unit of work for coordinating cross-repository transactions.</param>
    public LinQ2DbRepository(IServiceProvider sp, TContext context, IUnitOfWork<TContext>? uow = null) : base(sp, uow) {
        Context = context;

        var entity = typeof(TEntity);

        TableName = entity.GetCustomAttribute<TableAttribute>(false)?.Name ?? entity.Name.Pluralize();
    }

    /// <summary>
    ///     Gets the underlying <typeparamref name="TContext" />.
    /// </summary>
    protected virtual TContext Context { get; }

    /// <summary>
    ///     Gets the LINQ to DB table for the managed entity type.
    /// </summary>
    protected virtual ITable<TEntity> Table => field ??= Context.GetTable<TEntity>().TableName(TableName);

    /// <summary>
    ///     The table name used for CRUD operations, derived from <see cref="TableAttribute" /> or the pluralized entity name.
    /// </summary>
    public virtual string TableName { get; }

    /// <inheritdoc />
    public override IAsyncEnumerable<TEntity> AsAsyncEnumerable() { return Table.AsAsyncEnumerable(); }

    /// <inheritdoc />
    public override IQueryable<TEntity> AsQueryable() { return Table.AsQueryable(); }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override IAsyncEnumerable<TResult> SearchAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override async Task AddAsync(TEntity entity, CancellationToken ct = default) {
        switch (await Advisor.For<IRepositoryAddAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, this, entity, ct)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return;
            case AdviseResult.Continue:
            default:
                break;
        }

        if (UnitOfWork?.IsActive == true) {
            await Context.InsertAsync(entity, TableName, token: ct);
            return;
        }

        _pending.Add(new(async token => {
            await Context.InsertAsync(entity, TableName, token: token); return 1;
        }, entity));
    }

    /// <inheritdoc />
    public override async Task UpdateAsync(TEntity entity, CancellationToken ct = default) {
        switch (await Advisor.For<IRepositoryUpdateAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, this, entity, ct)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return;
            case AdviseResult.Continue:
            default:
                break;
        }

        if (UnitOfWork?.IsActive == true) {
            await Context.UpdateAsync(entity, TableName, token: ct);
            return;
        }

        _pending.Add(new(async token => {
            await Context.UpdateAsync(entity, TableName, token: token); return 1;
        }, entity));
    }

    /// <inheritdoc />
    public override async Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
        switch (await Advisor.For<IRepositoryRemoveAdvisor<TEntity>>()
                             .RunAsync(AdviceContext, this, entity, ct)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return;
            case AdviseResult.Continue:
            default:
                break;
        }

        if (UnitOfWork?.IsActive == true) {
            await Context.DeleteAsync(entity, TableName, token: ct);
            return;
        }

        _pending.Add(new(async token => {
            await Context.DeleteAsync(entity, TableName, token: token); return 1;
        }, entity));
    }

    /// <inheritdoc />
    public override async ValueTask<int> CommitAsync(CancellationToken ct = default) {
        if (UnitOfWork?.IsActive == true) {
            throw new InvalidOperationException("Commit is not allowed within a unit of work.");
        }

        if (_pending.Count == 0) {
            return 0;
        }

        var rows = 0;
        foreach (var op in _pending) {
            rows += await op.Execute(ct);
        }

        _pending.Clear();

        return rows;
    }

    /// <inheritdoc />
    public override void Detach(TEntity entity) { }

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

    #region Nested type: PendingOperation

    private readonly struct PendingOperation
    {
        public PendingOperation(Func<CancellationToken, Task<int>> execute, TEntity entity) {
            Execute = execute;
            Entity  = entity;
        }

        public Func<CancellationToken, Task<int>> Execute { get; }

        public TEntity Entity { get; }
    }

    #endregion
}
