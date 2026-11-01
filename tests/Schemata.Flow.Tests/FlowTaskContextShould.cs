using System;
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

public class FlowTaskContextShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reject_Source_Type_Mismatch_Before_Resolving_Requested_Repository(bool matching) {
        var binding = new SchemataProcessSource {
            Name = "source", Source = "sources/one",
            SourceType = matching ? typeof(Source).FullName! : typeof(SchemataProcess).FullName!,
        };
        var bindings = new Mock<IRepository<SchemataProcessSource>>();
        var sources = new Mock<IRepository<Source>>(MockBehavior.Strict);
        var services = new ServiceCollection().AddSingleton(bindings.Object).AddSingleton(sources.Object).BuildServiceProvider();
        var uow = Mock.Of<IUnitOfWork>();
        var source = new Source { CanonicalName = "sources/one" };
        sources.Setup(r => r.Join(uow));
        sources.Setup(r => r.SingleOrDefaultAsync(
                   It.IsAny<Func<System.Linq.IQueryable<Source>, System.Linq.IQueryable<Source>>>(),
                   It.IsAny<CancellationToken>(),
                   It.IsAny<QueryOptions>())).ReturnsAsync(source);
        var execution = new FlowExecutionContext(uow, services) {
            CreateProcessAsync = (_, _) => throw new NotSupportedException(),
            CreateTokenAsync = (_, _) => throw new NotSupportedException(),
            PersistSnapshotAsync = (_, _) => throw new NotSupportedException(),
            FindSourceBinding = (_, _, _) => binding,
        };
        var context = new FlowTaskContext(new(),
            new SchemataProcess { CanonicalName = "processes/one" },
            new SchemataProcessToken { CanonicalName = "tokens/one" }, execution);

        if (matching) {
            Assert.Same(source, await context.SourceAsync<Source>("source"));
        } else {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await context.SourceAsync<Source>("source"));
            Assert.Contains(binding.SourceType, error.Message);
            sources.Verify(r => r.Join(It.IsAny<IUnitOfWork>()), Times.Never);
        }
    }

    [Fact]
    public void Get_Required_Service_Joins_Repository_To_Unit_Of_Work() {
        var uow        = Mock.Of<IUnitOfWork>();
        var repository = new Mock<IRepository<SchemataProcessSource>>();
        var context    = Context(uow, new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider());

        var resolved = context.GetRequiredService<IRepository<SchemataProcessSource>>();

        Assert.Same(repository.Object, resolved);
        repository.Verify(r => r.Join(uow), Times.Once);
    }

    [Fact]
    public void Get_Service_Returns_Null_For_Unregistered_Service() {
        var context = Context(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider());

        Assert.Null(context.GetService<IRepository<SchemataProcessSource>>());
    }

    [Fact]
    public void Get_Required_Service_Resolves_Keyed_Registration() {
        var services = new ServiceCollection()
                      .AddKeyedSingleton("primary", new Widget("primary"))
                      .AddKeyedSingleton("fallback", new Widget("fallback"))
                      .BuildServiceProvider();
        var context = Context(Mock.Of<IUnitOfWork>(), services);

        Assert.Equal("fallback", context.GetRequiredService<Widget>("fallback").Name);
        Assert.Null(context.GetService<Widget>());
    }

    [Fact]
    public void Repository_Returns_Joined_Registered_Repository() {
        var uow        = Mock.Of<IUnitOfWork>();
        var repository = new Mock<IRepository<SchemataProcess>>();
        var context    = Context(uow, new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider());

        var resolved = context.Repository<SchemataProcess>();

        Assert.Same(repository.Object, resolved);
        repository.Verify(r => r.Join(uow), Times.Once);
    }

    private static FlowTaskContext Context(IUnitOfWork uow, IServiceProvider services) {
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var token = new SchemataProcessToken {
            Name          = "t1",
            CanonicalName = "processes/p1/tokens/t1",
            Process       = "p1",
            ScopeName     = string.Empty,
            StateName     = "s1",
        };
        return new(new(), process, token, FlowTestCreation.Context(uow, services));
    }

    private sealed record Widget(string Name);
    public sealed class Source : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }


}
