using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessPersistenceCompensationShould
{
    [Fact]
    public async Task Replace_Bindings_With_The_Current_Nonterminal_Snapshot() {
        var rows = new List<SchemataProcessCompensation> {
            new() {
                Process                 = "processes/p1",
                ScopeOwnerCanonicalName = "processes/p1",
                ActivityName            = "stale",
                RegistrationOrder       = 0,
            },
        };
        var process = Process("Running");
        var snapshot = new ProcessSnapshot {
            Process = process,
            Tokens = [],
            Transitions = [],
            CompensationBindings = [
                new("processes/p1", "first", 0),
                new("processes/p1", "second", 1),
            ],
        };

        await new ProcessPersistence().PersistSnapshotAsync(Scope(rows), snapshot, CancellationToken.None);

        Assert.Equal(
            snapshot.CompensationBindings,
            rows.Select(row => new ProcessCompensationBinding(
                            row.ScopeOwnerCanonicalName,
                            row.ActivityName,
                            row.RegistrationOrder)));
        Assert.All(rows, row => Assert.Equal(process.CanonicalName, row.Process));
    }

    [Fact]
    public async Task Remove_Bindings_When_The_Process_Is_Terminal() {
        var rows = new List<SchemataProcessCompensation> {
            new() {
                Process                 = "processes/p1",
                ScopeOwnerCanonicalName = "processes/p1",
                ActivityName            = "host",
                RegistrationOrder       = 0,
            },
        };
        var snapshot = new ProcessSnapshot {
            Process = Process("Completed"),
            Tokens = [],
            Transitions = [],
            CompensationBindings = [new("processes/p1", "host", 0)],
        };

        await new ProcessPersistence().PersistSnapshotAsync(Scope(rows), snapshot, CancellationToken.None);

        Assert.Empty(rows);
    }

    private static SchemataProcess Process(string state) {
        return new() {
            Name          = "p1",
            CanonicalName = "processes/p1",
            State         = state,
        };
    }

    private static FlowPersistenceScope Scope(List<SchemataProcessCompensation> compensations) {
        return new(
            Mock.Of<IUnitOfWork>(),
            Repository<SchemataProcess>().Object,
            Repository<SchemataProcessToken>().Object,
            Repository<SchemataProcessTransition>().Object,
            Repository<SchemataProcessSource>().Object,
            CompensationRepository(compensations).Object);
    }

    private static Mock<IRepository<T>> Repository<T>()
        where T : class {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<T?>((T?)null));
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repository;
    }

    private static Mock<IRepository<SchemataProcessCompensation>> CompensationRepository(
        List<SchemataProcessCompensation> rows
    ) {
        var repository = Repository<SchemataProcessCompensation>();
        repository.Setup(r => r.ListAsync<SchemataProcessCompensation>(
                             It.IsAny<Func<IQueryable<SchemataProcessCompensation>, IQueryable<SchemataProcessCompensation>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataProcessCompensation>, IQueryable<SchemataProcessCompensation>> query, CancellationToken _) =>
                      Async(query(rows.AsQueryable()).ToList()));
        repository.Setup(r => r.RemoveRangeAsync(
                             It.IsAny<IEnumerable<SchemataProcessCompensation>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((IEnumerable<SchemataProcessCompensation> values, CancellationToken _) => {
                      rows.RemoveAll(row => values.Contains(row));
                      return Task.CompletedTask;
                  });
        repository.Setup(r => r.AddRangeAsync(
                             It.IsAny<IEnumerable<SchemataProcessCompensation>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((IEnumerable<SchemataProcessCompensation> values, CancellationToken _) => {
                      rows.AddRange(values);
                      return Task.CompletedTask;
                  });
        return repository;
    }

    private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> values) {
        foreach (var value in values) {
            yield return value;
        }

        await Task.CompletedTask;
    }
}
