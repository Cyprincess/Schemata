using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;

namespace Schemata.Report.Tests.Fixtures;

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