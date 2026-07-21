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
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Resource.Tests;

public class PurgeJobShould
{
    [Fact]
    public async Task Invoke_WithParent_PersistsParentForPurgeJob() {
        string? argsJson = null;
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.TriggerAsync<PurgeJob<ParentTrashStudent>>(
                            It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                 .Callback((JobContext context, CancellationToken _) => argsJson = context.ArgsJson)
                 .ReturnsAsync(new SchemataJobExecution { Uid = Guid.NewGuid() });

        using var services = new ServiceCollection()
                            .AddSingleton(scheduler.Object)
                            .BuildServiceProvider();
        var handler = new PurgeHandler<ParentTrashStudent>(services);

        await handler.InvokeAsync(null, new PurgeRequest {
            Filter = "*",
            Parent = "schools/one",
        }, null, null, CancellationToken.None);

        using var document = JsonDocument.Parse(argsJson!);
        Assert.Equal("schools/one", document.RootElement.GetProperty(nameof(PurgeOperationArgs.Parent)).GetString());
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
        var job = new PurgeJob<ParentTrashStudent>(services);
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
