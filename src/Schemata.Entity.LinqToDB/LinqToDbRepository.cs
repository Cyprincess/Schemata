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
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     LINQ to DB implementation of <see cref="RepositoryBase{TEntity}" />.
/// </summary>
/// <remarks>
///     Mutations execute immediately against the data context inside an open transaction.
///     The transaction opens lazily on the first <see cref="AddAsync" /> / <see cref="UpdateAsync" />
///     / <see cref="RemoveAsync" /> when the repository owns its context; once enlisted via
///     <see cref="RepositoryBase{TEntity}.Join" />, the unit of work owns the transaction and
///     this repository simply executes statements against the shared connection.
/// </remarks>
/// <typeparam name="TContext">The <see cref="DataConnection" /> type.</typeparam>
/// <typeparam name="TEntity">The entity type managed by this repository.</typeparam>
public class LinqToDbRepository<TContext, TEntity> : RepositoryBase<TEntity>
    where TContext : DataConnection
    where TEntity : class
{
    private TContext                   _context;
    private DataConnectionTransaction? _txn;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LinqToDbRepository{TContext,TEntity}" /> class.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="factory">A factory that creates a new <typeparamref name="TContext" /> instance.</param>
    public LinqToDbRepository(IServiceProvider sp, Func<TContext> factory) : base(sp) {
        _context = factory();

        var entity = typeof(TEntity);

        TableName = entity.GetCustomAttribute<TableAttribute>(false)?.Name ?? entity.Name.Pluralize();
    }

    protected virtual TContext Context => _context;

    protected virtual ITable<TEntity> Table => field ??= Context.GetTable<TEntity>().TableName(TableName);

    /// <summary>
    ///     The table name used for CRUD operations, derived from <see cref="TableAttribute" /> or the pluralized entity name.
    /// </summary>
    public virtual string TableName { get; }

    protected override IQueryable<TEntity> AsQueryable() { return Table.AsQueryable(); }

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

        EnsureTransaction();

        await Context.InsertAsync(entity, TableName, token: ct);
    }

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

        TrackUpdate(entity);

        EnsureTransaction();

        await Context.UpdateAsync(entity, TableName, token: ct);
    }

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

        TrackRemove(entity);

        EnsureTransaction();

        await Context.DeleteAsync(entity, TableName, token: ct);
    }

    public override IUnitOfWork Begin() {
        var uow = ServiceProvider.GetRequiredService<IUnitOfWork<TContext>>();

        Join(uow);

        return uow;
    }

    public override async Task CommitAsync(CancellationToken ct = default) {
        if (!OwnsContext) {
            throw new InvalidOperationException("Repository is enlisted in a unit of work. Call IUnitOfWork.CommitAsync instead.");
        }

        var snapshot = SnapshotChanges();

        if (_txn is not null) {
            try {
                await _txn.CommitAsync(ct);
            } catch {
                try {
                    await _txn.RollbackAsync(CancellationToken.None);
                } catch {
                    // Transaction may already be completed; rollback during cleanup must not throw.
                }

                ResetTracking();

                throw;
            } finally {
                await _txn.DisposeAsync();
                _txn = null;
            }
        }

        await DispatchCommittedAsync(snapshot, ct);
    }

    /// <summary>
    ///     Opens the standalone transaction on first mutation. When enlisted, the unit of work
    ///     already owns the transaction; this is a no-op in that case.
    /// </summary>
    private void EnsureTransaction() {
        if (!OwnsContext) {
            return;
        }

        _txn ??= Context.BeginTransaction();
    }

    protected override void AttachContext(IUnitOfWork uow) {
        if (uow is not LinqToDbUnitOfWork<TContext> db) {
            throw new InvalidOperationException($"UoW of type {
                uow.GetType()
            } is not compatible with {
                typeof(TContext)
            }.");
        }

        // base.Join has already verified _added/_updated/_removed are empty before calling
        // this and EnsureTransaction is only triggered by mutations, so _txn is necessarily
        // null here.
        _context.Dispose();
        _context = db.Context;

        db.AddCommitSink(async ct => await DispatchCommittedAsync(SnapshotChanges(), ct));
        db.AddRollbackSink(ResetTracking);
    }

    protected override void DisposeContext() {
        if (_txn is not null) {
            try {
                _txn.Rollback();
            } catch {
                // Transaction may already be completed; rollback during cleanup must not throw.
            }

            _txn.Dispose();
            _txn = null;
        }

        _context.Dispose();
    }

    protected override async ValueTask DisposeContextAsync() {
        if (_txn is not null) {
            try {
                await _txn.RollbackAsync();
            } catch {
                // Transaction may already be completed; rollback during cleanup must not throw.
            }

            await _txn.DisposeAsync();
            _txn = null;
        }

        await _context.DisposeAsync();
    }

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
