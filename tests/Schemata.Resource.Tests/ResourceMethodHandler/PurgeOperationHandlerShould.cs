using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Expressions.Aip;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class PurgeOperationHandlerShould
{
    [Fact]
    public async Task RunAsync_Preview_CountsAndSamplesWithoutDeleting() {
        var rows             = new[] { DeletedStudent("alice-1"), DeletedStudent("bob-1") };
        var querySuppression = new Mock<IDisposable>();
        var repository       = Repository(rows);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(querySuppression.Object);
        var handler = new PurgeOperationHandler<TrashStudent>(Services(repository.Object));

        var result = Assert.IsType<PurgeResponse>(
            await handler.RunAsync(new() { Filter = "*" }, CancellationToken.None));

        Assert.Equal(2, result.PurgeCount);
        Assert.Equal(["trashStudents/alice-1", "trashStudents/bob-1"], result.PurgeSample);
        repository.Verify(r => r.SuppressQuerySoftDelete(), Times.Exactly(2));
        querySuppression.Verify(s => s.Dispose(), Times.Exactly(2));
        repository.Verify(r => r.RemoveAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Preview_CountsAndSamplesOnlySoftDeletedRows() {
        var rows       = new[] { Student("active-1"), DeletedStudent("deleted-1"), Student("active-2") };
        var repository = Repository(rows);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        var handler = new PurgeOperationHandler<TrashStudent>(Services(repository.Object));

        var result = Assert.IsType<PurgeResponse>(
            await handler.RunAsync(new() { Filter = "*" }, CancellationToken.None));

        Assert.Equal(1, result.PurgeCount);
        Assert.Equal(["trashStudents/deleted-1"], result.PurgeSample);
    }

    [Fact]
    public async Task RunAsync_Force_PhysicallyRemovesMatchesAndCommitsOnce() {
        var rows              = new[] { DeletedStudent("alice-1"), DeletedStudent("bob-1") };
        var querySuppression  = new Mock<IDisposable>();
        var removeSuppression = new Mock<IDisposable>();
        var repository        = Repository(rows);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(querySuppression.Object);
        repository.Setup(r => r.SuppressSoftDelete()).Returns(removeSuppression.Object);
        repository.Setup(r => r.RemoveAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var handler = new PurgeOperationHandler<TrashStudent>(Services(repository.Object));

        var result = Assert.IsType<PurgeResponse>(
            await handler.RunAsync(new() { Filter = "*", Force = true }, CancellationToken.None));

        Assert.Equal(2, result.PurgeCount);
        Assert.Empty(result.PurgeSample);
        repository.Verify(r => r.SuppressQuerySoftDelete(), Times.Exactly(2));
        repository.Verify(r => r.SuppressSoftDelete(), Times.Exactly(2));
        repository.Verify(r => r.RemoveAsync(rows[0], CancellationToken.None), Times.Once);
        repository.Verify(r => r.RemoveAsync(rows[1], CancellationToken.None), Times.Once);
        repository.Verify(r => r.CommitAsync(CancellationToken.None), Times.Once);
        querySuppression.Verify(s => s.Dispose(), Times.Exactly(2));
        removeSuppression.Verify(s => s.Dispose(), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_Force_PhysicallyRemovesOnlySoftDeletedRows() {
        var active     = Student("active-1");
        var deleted    = DeletedStudent("deleted-1");
        var rows       = new[] { active, deleted };
        var repository = Repository(rows);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SuppressSoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.RemoveAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var handler = new PurgeOperationHandler<TrashStudent>(Services(repository.Object));

        await handler.RunAsync(new() { Filter = "*", Force = true }, CancellationToken.None);

        repository.Verify(r => r.RemoveAsync(active, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.RemoveAsync(deleted, CancellationToken.None), Times.Once);
    }

    private static IServiceProvider Services(IRepository<TrashStudent> repository) {
        return new ServiceCollection().AddAipExpressions().AddSingleton(repository).BuildServiceProvider();
    }

    private static TrashStudent Student(string name) {
        return new() { Name = name, CanonicalName = $"trashStudents/{name}" };
    }

    private static TrashStudent DeletedStudent(string name) {
        return new() { Name = name, CanonicalName = $"trashStudents/{name}", DeleteTime = DateTime.UtcNow };
    }

    private static Mock<IRepository<TrashStudent>> Repository(IReadOnlyCollection<TrashStudent> rows) {
        var repository = new Mock<IRepository<TrashStudent>>();
        repository
           .Setup(r => r.LongCountAsync(It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                                        It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>> predicate, CancellationToken _)
                        => new(predicate(rows.AsQueryable()).LongCount()));
        repository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                                   It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>> predicate, CancellationToken _)
                        => ToAsync(predicate(rows.AsQueryable())));
        return repository;
    }

    private static async IAsyncEnumerable<TrashStudent> ToAsync(IEnumerable<TrashStudent> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }
}
