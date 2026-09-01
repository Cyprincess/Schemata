using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Resource.Tests;

public class PurgeJobShould
{
    [Fact]
    public async Task Invoke_Persists_Request_And_Returns_Pending_Operation() {
        JobContext? staged = null;
        var uid = Guid.NewGuid();
        var execution = new SchemataJobExecution {
            Uid           = uid,
            Name          = uid.ToString("n"),
            CanonicalName = $"operations/{uid:n}",
            State         = ExecutionState.Pending,
        };
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.TriggerAsync<PurgeJob<ParentTrashStudent>>(
                            It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                 .Callback((JobContext context, CancellationToken _) => staged = context)
                 .ReturnsAsync(execution);

        using var services = new ServiceCollection()
                            .AddSingleton(scheduler.Object)
                            .BuildServiceProvider();
        var handler = new PurgeHandler<ParentTrashStudent>(services);

        var operation = await handler.HandleAsync(
            new PurgeResourceRequest<ParentTrashStudent> {
                Filter   = "*",
                Language = "aip",
                Parent   = "schools/one",
                Force    = false,
            },
            CancellationToken.None);

        Assert.NotNull(staged);
        Assert.Equal("purge", staged.Method);
        Assert.NotEqual(Guid.Empty, staged.ExecutionUid);
        var args = JsonSerializer.Deserialize<PurgeOperationArgs>(staged.ArgsJson!, SchemataJson.Default);
        Assert.NotNull(args);
        Assert.Equal("*", args.Filter);
        Assert.Equal("aip", args.Language);
        Assert.Equal("schools/one", args.Parent);
        Assert.False(args.Force);
        Assert.Equal(execution.CanonicalName, operation.CanonicalName);
        Assert.False(operation.Done);
    }

    [Fact]
    public async Task Preview_WithParent_OnlyCountsAndSamplesMatchingChildren() {
        var rows = new[] {
            Entity("one", "a"),
            Entity("two", "b"),
        };
        var repository = new Mock<IRepository<ParentTrashStudent>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.LongCountAsync(
                              It.IsAny<Func<IQueryable<ParentTrashStudent>, IQueryable<ParentTrashStudent>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<ParentTrashStudent>, IQueryable<ParentTrashStudent>> query, CancellationToken _) =>
                      new ValueTask<long>(query(rows.AsQueryable()).LongCount()));
        repository.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<ParentTrashStudent>, IQueryable<ParentTrashStudent>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<ParentTrashStudent>, IQueryable<ParentTrashStudent>> query, CancellationToken _) =>
                      ToAsyncEnumerable(query(rows.AsQueryable())));

        using var services = new ServiceCollection()
                            .AddSingleton(repository.Object)
                            .BuildServiceProvider();
        var job = new PurgeJob<ParentTrashStudent>(repository.Object, services);
        var execution = new SchemataJobExecution();

        await job.ExecuteAsync(new JobContext {
            ArgsJson = "{\"filter\":\"*\",\"parent\":\"schools/one\",\"force\":false}",
            Execution = execution,
        }, CancellationToken.None);

        var result = JsonSerializer.Deserialize<PurgeResponse>(execution.Output!, SchemataJson.Default);
        Assert.NotNull(result);
        Assert.Equal(1, result.PurgeCount);
        Assert.Equal(["schools/one/students/a"], result.PurgeSample);
        repository.Verify(r => r.RemoveAsync(It.IsAny<ParentTrashStudent>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Force_WithParent_RemovesMatchingChildren_And_CommitsOnce() {
        var matching = Entity("one", "a");
        var other    = Entity("two", "b");
        var rows     = new[] { matching, other };
        var repository = new Mock<IRepository<ParentTrashStudent>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SuppressSoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.LongCountAsync(
                              It.IsAny<Func<IQueryable<ParentTrashStudent>, IQueryable<ParentTrashStudent>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<ParentTrashStudent>, IQueryable<ParentTrashStudent>> query, CancellationToken _) =>
                      new ValueTask<long>(query(rows.AsQueryable()).LongCount()));
        repository.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<ParentTrashStudent>, IQueryable<ParentTrashStudent>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<ParentTrashStudent>, IQueryable<ParentTrashStudent>> query, CancellationToken _) =>
                      ToAsyncEnumerable(query(rows.AsQueryable())));
        repository.Setup(r => r.RemoveAsync(It.IsAny<ParentTrashStudent>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        using var services = new ServiceCollection()
                            .AddSingleton(repository.Object)
                            .BuildServiceProvider();
        var job       = new PurgeJob<ParentTrashStudent>(repository.Object, services);
        var execution = new SchemataJobExecution();

        await job.ExecuteAsync(new JobContext {
            ArgsJson  = "{\"filter\":\"*\",\"parent\":\"schools/one\",\"force\":true}",
            Execution = execution,
        }, CancellationToken.None);

        var result = JsonSerializer.Deserialize<PurgeResponse>(execution.Output!, SchemataJson.Default);
        Assert.NotNull(result);
        Assert.Equal(1, result.PurgeCount);
        Assert.Empty(result.PurgeSample);
        repository.Verify(r => r.RemoveAsync(matching, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.RemoveAsync(other, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ParentTrashStudent Entity(string school, string name) {
        return new() {
            School = school,
            Name = name,
            CanonicalName = $"schools/{school}/students/{name}",
            DeleteTime = DateTime.UtcNow,
        };
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source) {
        foreach (var item in source) {
            yield return item;
            await Task.Yield();
        }
    }

    [CanonicalName("schools/{school}/students/{student}")]
    public sealed class ParentTrashStudent : ICanonicalName, ISoftDelete
    {
        public string? School        { get; set; }
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public DateTime? DeleteTime  { get; set; }
        public DateTime? PurgeTime   { get; set; }
    }
}
