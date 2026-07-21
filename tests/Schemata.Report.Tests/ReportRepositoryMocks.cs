using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;

namespace Schemata.Report.Tests;

internal static class ReportRepositoryMocks
{
    internal static IRepository<TEntity> Create<TEntity>(
        List<TEntity>                 records,
        ReportRepositoryTransactions  transactions,
        Action?                       onCommit = null,
        Action<TEntity>?              onUpdate = null
    )
        where TEntity : class {
        var pending = new List<TEntity>();
        var removed = new List<TEntity>();
        var services = new Mock<IServiceProvider>(MockBehavior.Strict);
        var disposable = new Mock<IDisposable>(MockBehavior.Strict);
        var repository = new Mock<IRepository<TEntity>>(MockBehavior.Strict);
        IUnitOfWork? unit = null;
        var committed = false;

        services.Setup(value => value.GetService(It.IsAny<Type>())).Returns((object?)null);
        disposable.Setup(value => value.Dispose());
        repository.SetupGet(value => value.AdviceContext).Returns(new AdviceContext(services.Object));
        repository.Setup(value => value.Begin()).Returns(() => {
            unit = transactions.Create(Commit);
            return unit;
        });
        repository.Setup(value => value.Join(It.IsAny<IUnitOfWork>())).Callback<IUnitOfWork>(joined => {
            transactions.Join(joined, Commit);
            unit = joined;
        });
        repository.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken _) => {
                      EnsureOpen();
                      if (unit is not null) {
                          transactions.Commit(unit);
                      } else {
                          Commit();
                      }

                      return Task.CompletedTask;
                  });
        repository.Setup(value => value.ListAsync<TEntity>(
                      It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                      It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TEntity>, IQueryable<TEntity>>? predicate, CancellationToken _) =>
                      ReportTestRows.ToAsync<TEntity>(predicate is null ? records : predicate(records.AsQueryable())));
        repository.Setup(value => value.FirstOrDefaultAsync<TEntity>(
                      It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                      It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TEntity>, IQueryable<TEntity>>? predicate, CancellationToken _) =>
                      ValueTask.FromResult<TEntity?>(
                          (predicate is null ? records.AsQueryable() : predicate(records.AsQueryable())).FirstOrDefault()));
        repository.Setup(value => value.AddAsync(It.IsAny<TEntity>(), It.IsAny<CancellationToken>()))
                  .Callback<TEntity, CancellationToken>((entity, _) => {
                      EnsureOpen();
                      pending.Add(entity);
                  })
                  .Returns(Task.CompletedTask);
        repository.Setup(value => value.UpdateAsync(It.IsAny<TEntity>(), It.IsAny<CancellationToken>()))
                  .Callback<TEntity, CancellationToken>((entity, _) => {
                      EnsureOpen();
                      onUpdate?.Invoke(entity);
                  })
                  .Returns(Task.CompletedTask);
        repository.Setup(value => value.RemoveRangeAsync(It.IsAny<IEnumerable<TEntity>>(), It.IsAny<CancellationToken>()))
                  .Callback<IEnumerable<TEntity>, CancellationToken>((entities, _) => {
                      EnsureOpen();
                      if (unit is null) {
                          foreach (var entity in entities) {
                              records.Remove(entity);
                          }
                      } else {
                          removed.AddRange(entities);
                      }
                  })
                  .Returns(Task.CompletedTask);
        repository.Setup(value => value.SuppressAddValidation()).Returns(disposable.Object);
        repository.Setup(value => value.SuppressUpdateValidation()).Returns(disposable.Object);
        repository.Setup(value => value.SuppressQuerySoftDelete()).Returns(disposable.Object);
        repository.Setup(value => value.SuppressSoftDelete()).Returns(disposable.Object);
        repository.Setup(value => value.SuppressTimestamp()).Returns(disposable.Object);
        repository.Setup(value => value.Dispose());
        repository.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return repository.Object;

        void EnsureOpen() {
            if (committed) {
                throw new InvalidOperationException("Repository was reused after commit.");
            }
        }

        void Commit() {
            records.AddRange(pending);
            pending.Clear();
            foreach (var entity in removed) {
                records.Remove(entity);
            }

            removed.Clear();
            committed = true;
            onCommit?.Invoke();
        }
    }
}

internal sealed class ReportRepositoryTransactions
{
    private readonly Dictionary<IUnitOfWork, List<Action>> _commits = [];

    internal IUnitOfWork Create(Action commit) {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _commits.Add(unit.Object, [commit]);
        unit.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Commit(unit.Object))
            .Returns(Task.CompletedTask);
        unit.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(value => value.Dispose());
        unit.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return unit.Object;
    }

    internal void Join(IUnitOfWork unit, Action commit) {
        if (!_commits.TryGetValue(unit, out var commits)) {
            throw new NotSupportedException();
        }

        commits.Add(commit);
    }

    internal void Commit(IUnitOfWork unit) {
        if (!_commits.TryGetValue(unit, out var commits)) {
            throw new NotSupportedException();
        }

        foreach (var commit in commits) {
            commit();
        }
    }
}
