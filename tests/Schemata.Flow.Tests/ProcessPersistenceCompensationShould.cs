using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Abstractions.Entities;
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

    [Fact]
    public async Task Preserve_Unchanged_Binding_Identity_And_Duplicate_Multiplicity() {
        var retained = new SchemataProcessCompensation {
            Uid = Guid.NewGuid(), Name = "consumer-binding", CanonicalName = "compensations/consumer-binding",
            Process = "processes/p1", ScopeOwnerCanonicalName = "processes/p1", ActivityName = "activity", RegistrationOrder = 2,
        };
        var rows = new List<SchemataProcessCompensation> { retained };
        var snapshot = new ProcessSnapshot {
            Process = Process("Running"), Tokens = [], Transitions = [],
            CompensationBindings = [new("processes/p1", "activity", 2), new("processes/p1", "activity", 2)],
        };
        var persistence = new ProcessPersistence();
        await persistence.PersistSnapshotAsync(Scope(rows), snapshot, default);
        Assert.Contains(rows, row => ReferenceEquals(row, retained));
        Assert.Equal(2, rows.Count);
        var uid = retained.Uid;
        await persistence.PersistSnapshotAsync(Scope(rows), snapshot, default);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Uid == uid && row.Name == "consumer-binding");
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
            CompensationRepository(compensations).Object,
            Services(compensations));
    }

    private static IServiceProvider Services(List<SchemataProcessCompensation> rows) {
        var services = new ServiceCollection();
        services.AddSingleton(Mutation<SchemataProcess>().Object);
        services.AddSingleton(Mutation<SchemataProcessToken>().Object);
        services.AddSingleton(Mutation<SchemataProcessTransition>().Object);
        services.AddSingleton(Mutation<SchemataProcessSource>().Object);
        services.AddSingleton(CompensationMutation(rows).Object);
        return services.BuildServiceProvider();
    }

    private static Mock<IResourceMutation<SchemataProcessCompensation>> CompensationMutation(
        List<SchemataProcessCompensation> rows
    ) {
        var mutation = Mutation<SchemataProcessCompensation>();
        mutation.Setup(m => m.CreateAsync(
                           It.IsAny<SchemataProcessCompensation>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                .Callback((SchemataProcessCompensation row, IUnitOfWork? _, CancellationToken _) => rows.Add(row))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(m => m.DeleteAsync(
                           It.IsAny<SchemataProcessCompensation>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                           It.IsAny<CancellationToken>()))
                .Callback((SchemataProcessCompensation row, IUnitOfWork? _, Operations _, CancellationToken _) => rows.Remove(row))
                .ReturnsAsync(MutationResult.Applied);
        return mutation;
    }

    private static Mock<IResourceMutation<T>> Mutation<T>()
        where T : class {
        var mutation = new Mock<IResourceMutation<T>>();
        mutation.Setup(m => m.CreateAsync(It.IsAny<T>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(m => m.UpdateAsync(
                       It.IsAny<T>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(m => m.DeleteAsync(
                       It.IsAny<T>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        return mutation;
    }

    private static Mock<IRepository<T>> Repository<T>()
        where T : class {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<T?>((T?)null));
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
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
        return repository;
    }

    private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> values) {
        foreach (var value in values) {
            yield return value;
        }

        await Task.CompletedTask;
    }
}
