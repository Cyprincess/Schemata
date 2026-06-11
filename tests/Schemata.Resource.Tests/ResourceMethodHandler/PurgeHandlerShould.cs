using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Expressions.Aip;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class PurgeHandlerShould
{
    [Fact]
    public async Task Invoke_Preview_CountsAndSamplesMatchesWithoutDeleting() {
        var rows = new[] {
            DeletedStudent("alice-1"),
            DeletedStudent("bob-1"),
        };
        var querySuppression = new Mock<IDisposable>();
        var repository = Repository(rows);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(querySuppression.Object);
        var dispatcher = new ImmediateOperationDispatcher();
        var services   = Services(repository.Object, dispatcher);
        var handler    = new PurgeHandler<TrashStudent>(services);

        var response = await handler.InvokeAsync(null, new() { Filter = "*" }, null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None);

        Assert.Equal("operations/test-operation", response.Operation);
        var result = Assert.IsType<PurgeResponse>(dispatcher.Result);
        Assert.Equal(2, result.PurgeCount);
        Assert.Equal(["trashStudents/alice-1", "trashStudents/bob-1"], result.PurgeSample);
        repository.Verify(r => r.SuppressQuerySoftDelete(), Times.Exactly(2));
        querySuppression.Verify(s => s.Dispose(), Times.Exactly(2));
        repository.Verify(r => r.RemoveAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_Preview_CountsAndSamplesOnlySoftDeletedRows() {
        var rows = new[] {
            Student("active-1"),
            DeletedStudent("deleted-1"),
            Student("active-2"),
        };
        var repository = Repository(rows);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        var dispatcher = new ImmediateOperationDispatcher();
        var services   = Services(repository.Object, dispatcher);
        var handler    = new PurgeHandler<TrashStudent>(services);

        await handler.InvokeAsync(null, new() { Filter = "*" }, null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None);

        var result = Assert.IsType<PurgeResponse>(dispatcher.Result);
        Assert.Equal(1, result.PurgeCount);
        Assert.Equal(["trashStudents/deleted-1"], result.PurgeSample);
    }

    [Fact]
    public async Task Invoke_Force_PhysicallyRemovesMatchesAndCommitsOnce() {
        var rows = new[] {
            DeletedStudent("alice-1"),
            DeletedStudent("bob-1"),
        };
        var querySuppression  = new Mock<IDisposable>();
        var removeSuppression = new Mock<IDisposable>();
        var repository        = Repository(rows);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(querySuppression.Object);
        repository.Setup(r => r.SuppressSoftDelete()).Returns(removeSuppression.Object);
        repository.Setup(r => r.RemoveAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var dispatcher = new ImmediateOperationDispatcher();
        var services   = Services(repository.Object, dispatcher);
        var handler    = new PurgeHandler<TrashStudent>(services);

        var response = await handler.InvokeAsync(null, new() { Filter = "*", Force = true }, null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None);

        Assert.Equal("operations/test-operation", response.Operation);
        var result = Assert.IsType<PurgeResponse>(dispatcher.Result);
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
    public async Task Invoke_Force_PhysicallyRemovesOnlySoftDeletedRows() {
        var active  = Student("active-1");
        var deleted = DeletedStudent("deleted-1");
        var rows = new[] {
            active,
            deleted,
        };
        var repository = Repository(rows);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SuppressSoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.RemoveAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var dispatcher = new ImmediateOperationDispatcher();
        var services   = Services(repository.Object, dispatcher);
        var handler    = new PurgeHandler<TrashStudent>(services);

        await handler.InvokeAsync(null, new() { Filter = "*", Force = true }, null, Mock.Of<ClaimsPrincipal>(), CancellationToken.None);

        var result = Assert.IsType<PurgeResponse>(dispatcher.Result);
        Assert.Equal(1, result.PurgeCount);
        repository.Verify(r => r.RemoveAsync(active, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.RemoveAsync(deleted, CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invoke_EmptyFilter_ThrowsValidationException(string? filter) {
        var repository = Repository([]);
        var services   = Services(repository.Object, new());
        var handler    = new PurgeHandler<TrashStudent>(services);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.InvokeAsync(
            null, new() { Filter = filter }, null, null, CancellationToken.None).AsTask());

        AssertInvalidFilter(ex);
    }

    [Fact]
    public async Task Invoke_InvalidFilter_ThrowsValidationException() {
        var repository = Repository([]);
        var services   = Services(repository.Object, new());
        var handler    = new PurgeHandler<TrashStudent>(services);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.InvokeAsync(
            null, new() { Filter = "(" }, null, null, CancellationToken.None).AsTask());

        AssertInvalidFilter(ex);
    }

    [Fact]
    public async Task Invoke_MissingDispatcher_ThrowsBridgeMessage() {
        var repository = Repository([]);
        var services = new ServiceCollection()
                      .AddAipExpressions()
                      .AddSingleton(repository.Object)
                      .BuildServiceProvider();
        var handler = new PurgeHandler<TrashStudent>(services);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.InvokeAsync(
            null, new() { Filter = "*" }, null, null, CancellationToken.None).AsTask());

        Assert.Contains("IOperationDispatcher", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Schemata.Scheduling.Http/Grpc", ex.Message, StringComparison.Ordinal);
    }

    private static TrashStudent Student(string name) {
        return new() { Name = name, CanonicalName = $"trashStudents/{name}" };
    }

    private static TrashStudent DeletedStudent(string name) {
        return new() { Name = name, CanonicalName = $"trashStudents/{name}", DeleteTime = DateTime.UtcNow };
    }

    private static void AssertInvalidFilter(ValidationException ex) {
        var detail = Assert.IsType<BadRequestDetail>(Assert.Single(ex.Details!));
        Assert.NotNull(detail.FieldViolations);
        Assert.Contains(detail.FieldViolations, e => e.Reason == FieldReasons.InvalidFilter);
    }

    private static Mock<IRepository<TrashStudent>> Repository(IReadOnlyCollection<TrashStudent> rows) {
        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.LongCountAsync(It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>> predicate, CancellationToken _) =>
                      new(predicate(rows.AsQueryable()).LongCount()));
        repository.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>> predicate, CancellationToken _) =>
                      ToAsync(predicate(rows.AsQueryable())));
        return repository;
    }

    private static IServiceProvider Services(IRepository<TrashStudent> repository, ImmediateOperationDispatcher dispatcher) {
        var provider = new ServiceCollection()
                      .AddAipExpressions()
                      .AddSingleton(repository)
                      .AddSingleton<IOperationDispatcher>(dispatcher)
                      .BuildServiceProvider();
        dispatcher.Services = provider;
        return provider;
    }

    private static async IAsyncEnumerable<TrashStudent> ToAsync(IEnumerable<TrashStudent> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class ImmediateOperationDispatcher : IOperationDispatcher
    {
        public IServiceProvider Services { get; set; } = null!;

        public object? Result { get; private set; }

        public async Task<string> DispatchAsync<TResult>(
            string operation,
            Func<IServiceProvider, CancellationToken, Task<TResult?>> work,
            CancellationToken ct
        ) where TResult : class {
            Result = await work(Services, ct);
            return "operations/test-operation";
        }
    }
}
