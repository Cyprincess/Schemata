using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;

namespace Schemata.Resource.Tests.Fixtures;

/// <summary>
///     Builds a <see cref="IUnitOfWork" /> double honoring the repository contract's settlement
///     timing: <c>CommitAsync</c> runs the registered commit sinks and disposal runs the rollback
///     sinks, so tests observe which completion evidence a handler-owned transaction produces.
/// </summary>
public static class UnitOfWorkMock
{
    public static Mock<IUnitOfWork> Create() {
        var transaction = new Mock<IUnitOfWork>();
        var committed   = new List<Func<CancellationToken, Task>>();
        var rollbacks   = new List<Action>();
        transaction.Setup(t => t.AddCommitSink(It.IsAny<int>(), It.IsAny<Func<CancellationToken, Task>>()))
                   .Callback<int, Func<CancellationToken, Task>>((_, sink) => committed.Add(sink));
        transaction.Setup(t => t.AddRollbackSink(It.IsAny<Action>()))
                   .Callback<Action>(rollbacks.Add);
        transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
                   .Callback(() => {
                       foreach (var sink in committed.ToArray()) {
                           sink(CancellationToken.None).GetAwaiter().GetResult();
                       }
                   })
                   .Returns(Task.CompletedTask);
        transaction.Setup(t => t.DisposeAsync())
                   .Callback(() => {
                       foreach (var reset in rollbacks) {
                           reset();
                       }
                   })
                   .Returns(ValueTask.CompletedTask);
        return transaction;
    }

    /// <summary>
    ///     Builds a repository double whose <see cref="IRepository.Begin" /> returns
    ///     <paramref name="transaction" />.
    /// </summary>
    public static Mock<IRepository<TEntity>> Repository<TEntity>(Mock<IUnitOfWork> transaction)
        where TEntity : class {
        var repository = new Mock<IRepository<TEntity>>();
        repository.Setup(r => r.Begin()).Returns(transaction.Object);
        return repository;
    }
}
