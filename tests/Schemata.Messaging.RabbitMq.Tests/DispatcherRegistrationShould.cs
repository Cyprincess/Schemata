using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Messaging.RabbitMq.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Messaging.RabbitMq.Tests;

/// <summary>
///     Asserts <see cref="RabbitMqRequestDispatcher" /> answers all three dispatcher contracts and
///     that a module capability extension's in-process registration block never displaces it once
///     staged first — the same <c>TryAdd</c>-wins-first semantics a Schemata module extension relies
///     on. The connection provider connects lazily on first <c>SendAsync</c>, so building and
///     resolving the dispatcher here never touches a real broker.
/// </summary>
public class DispatcherRegistrationShould
{
    [Fact]
    public async Task Resolve_AllThreeDispatcherContracts_ToTheSameRabbitMqInstance() {
        var services = new ServiceCollection();
        services.AddRabbitMqTransport();
        services.AddRabbitMqRequestDispatcher(_ => { });

        await using var provider = services.BuildServiceProvider();
        await using var scope    = provider.CreateAsyncScope();

        var request = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var command = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        var query   = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

        Assert.IsType<RabbitMqRequestDispatcher>(request);
        Assert.Same(request, command);
        Assert.Same(request, query);
    }

    [Fact]
    public async Task KeepResolvingToRabbitMq_WhenAModuleExtensionRegistersInProcessAfterward() {
        var services = new ServiceCollection();
        services.AddRabbitMqTransport();
        services.AddRabbitMqRequestDispatcher(_ => { });

        // The four-line block every module capability extension (AddSchemataFlow and friends)
        // adds. TryAdd means the RabbitMQ registration staged above wins.
        AddModuleInProcessDispatcherBlock(services);

        await using var provider = services.BuildServiceProvider();
        await using var scope    = provider.CreateAsyncScope();

        Assert.IsType<RabbitMqRequestDispatcher>(scope.ServiceProvider.GetRequiredService<IRequestDispatcher>());
        Assert.IsType<RabbitMqRequestDispatcher>(scope.ServiceProvider.GetRequiredService<ICommandDispatcher>());
        Assert.IsType<RabbitMqRequestDispatcher>(scope.ServiceProvider.GetRequiredService<IQueryDispatcher>());
    }

    [Fact]
    public void FallBackToInProcess_WhenRabbitMqWasNeverRegistered() {
        var services = new ServiceCollection();
        AddModuleInProcessDispatcherBlock(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<InProcessRequestDispatcher>(scope.ServiceProvider.GetRequiredService<IRequestDispatcher>());
        Assert.IsType<InProcessRequestDispatcher>(scope.ServiceProvider.GetRequiredService<ICommandDispatcher>());
        Assert.IsType<InProcessRequestDispatcher>(scope.ServiceProvider.GetRequiredService<IQueryDispatcher>());
    }

    private static void AddModuleInProcessDispatcherBlock(IServiceCollection services) {
        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
    }
}
