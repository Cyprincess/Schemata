using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Skeleton;
using Schemata.Flow.Event.Internal;
using Schemata.Flow.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessEventLifecycleObserverShould
{
    [Fact]
    public async Task OnStartedAsync_MissingEventBus_Throws() {
        var observer = new ProcessEventLifecycleObserver(new ServiceCollection().BuildServiceProvider());
        var process = new SchemataProcess {
            CanonicalName  = "processes/p1",
            DefinitionName = "approval",
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => observer.OnStartedAsync(process, CancellationToken.None));

        Assert.Contains(nameof(IEventBus), exception.Message);
    }
}
