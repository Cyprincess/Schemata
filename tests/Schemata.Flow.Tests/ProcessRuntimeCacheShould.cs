using System.Threading.Tasks;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessRuntimeCacheShould
{
    [Fact]
    public async Task Invalidate_RemovesCachedInstance() {
        var fixture = new ProcessRuntimeFixture();
        var process = await fixture.Runtime.StartProcessInstanceAsync("approval");
        ProcessRuntimeFixture.MutatePersisted(fixture.Persisted[0], "external", "External");

        fixture.Runtime.Invalidate(process.CanonicalName!);
        await fixture.Runtime.CompleteActivityAsync(process.CanonicalName!);

        Assert.Equal("external", fixture.AdvancedStateId);
    }

    [Fact]
    public async Task ReloadAsync_BypassesCache_RefreshesFromRepo() {
        var fixture = new ProcessRuntimeFixture();
        var process = await fixture.Runtime.StartProcessInstanceAsync("approval");
        ProcessRuntimeFixture.MutatePersisted(fixture.Persisted[0], "external", "External");

        var reloaded = await fixture.Runtime.ReloadAsync(process.CanonicalName!, default);
        Assert.Equal("external", reloaded?.StateId);

        await fixture.Runtime.CompleteActivityAsync(process.CanonicalName!);

        Assert.Equal("external", fixture.AdvancedStateId);
    }
}
