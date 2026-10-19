using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowSourceReadScopeShould
{
    [Fact]
    public async Task SourceRead_PassesOwnerAndSoftDeleteOverrides() {
        QueryOptions? observed = null;
        var context = Context(Execution(SourceRepository(options => observed = options)));
        await context.SourceAsync<Source>("source");

        Assert.True(observed is { IgnoreOwner: true, IgnoreSoftDelete: true });
    }

    [Fact]
    public async Task Nested_Plain_Query_Inside_SourceRead_Keeps_DefaultOptions() {
        QueryOptions? sourceOptions = null;
        QueryOptions? nestedOptions = null;
        var sources = new Mock<IRepository<Source>>();
        sources.Setup(r => r.SingleOrDefaultAsync(
                   It.IsAny<Func<IQueryable<Source>, IQueryable<Source>>>(),
                   It.IsAny<CancellationToken>(),
                   It.IsAny<QueryOptions>()))
               .Returns((Func<IQueryable<Source>, IQueryable<Source>> _, CancellationToken _, QueryOptions options) =>
                   new ValueTask<Source?>(ReadAsync(options)));

        async Task<Source?> ReadAsync(QueryOptions options) {
            if (options == default) {
                nestedOptions = options;
                return null;
            }

            sourceOptions = options;
            await sources.Object.SingleOrDefaultAsync(q => q, default);
            return new Source { CanonicalName = "sources/one" };
        }

        var execution = Execution(sources);
        var context = Context(execution);

        await context.SourceAsync<Source>("source");

        Assert.True(sourceOptions is { IgnoreOwner: true, IgnoreSoftDelete: true });
        Assert.Equal(default(QueryOptions), nestedOptions);
    }

    [Fact]
    public async Task Parallel_SourceReads_On_Distinct_Repositories_Keep_Their_Own_Options() {
        QueryOptions? first  = null;
        QueryOptions? second = null;
        var left  = SourceRepository(options => first = options);
        var right = SourceRepository(options => second = options);

        await Task.WhenAll(
            Context(Execution(left)).SourceAsync<Source>("source").AsTask(),
            Context(Execution(right)).SourceAsync<Source>("source").AsTask());

        Assert.True(first is { IgnoreOwner: true, IgnoreSoftDelete: true });
        Assert.True(second is { IgnoreOwner: true, IgnoreSoftDelete: true });
    }

    private static Mock<IRepository<Source>> SourceRepository(Action<QueryOptions> observe) {
        var repository = new Mock<IRepository<Source>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.SingleOrDefaultAsync(
                      It.IsAny<Func<IQueryable<Source>, IQueryable<Source>>>(),
                      It.IsAny<CancellationToken>(),
                      It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<Source>, IQueryable<Source>> _, CancellationToken _, QueryOptions options) => {
                      observe(options);
                      return new ValueTask<Source?>(new Source { CanonicalName = "sources/one" });
                  });
        return repository;
    }

    private static FlowExecutionContext Execution(Mock<IRepository<Source>> sources) {
        var services = new ServiceCollection()
                      .AddSingleton(sources.Object)
                      .AddSingleton(new Mock<IRepository<SchemataProcessSource>>().Object)
                      .BuildServiceProvider();
        return new(Mock.Of<IUnitOfWork>(), services) {
            CreateProcessAsync  = (_, _) => Task.CompletedTask,
            CreateTokenAsync    = (_, _) => Task.CompletedTask,
            PersistSnapshotAsync = (_, _) => Task.CompletedTask,
            FindSourceBinding   = (_, _, _) => new SchemataProcessSource {
                Name       = "source",
                Source     = "sources/one",
                SourceType = typeof(Source).FullName!,
            },
        };
    }

    private static FlowTaskContext Context(FlowExecutionContext execution) {
        return new(new(),
                   new SchemataProcess { CanonicalName = "processes/one" },
                   new SchemataProcessToken { CanonicalName = "tokens/one" },
                   execution);
    }

    public sealed class Source : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
