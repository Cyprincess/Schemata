using System;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowTaskContextShould
{
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
            ScopeName     = "p1",
            StateName     = "s1",
        };
        return new(new(), process, token, new(uow, services));
    }

    private sealed record Widget(string Name);

}
