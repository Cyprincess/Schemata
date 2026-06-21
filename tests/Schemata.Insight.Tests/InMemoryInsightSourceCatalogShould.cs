using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class InMemoryInsightSourceCatalogShould
{
    [Fact]
    public async Task Resolves_A_Registered_Name() {
        var config  = new SourceConfig("repository", new Dictionary<string, object?> { ["resource"] = "customers" });
        var catalog = new InMemoryInsightSourceCatalog(new Dictionary<string, SourceConfig> { ["customers"] = config });

        var resolved = await catalog.ResolveAsync("customers", CancellationToken.None);

        Assert.Same(config, resolved);
    }

    [Fact]
    public async Task Returns_Null_For_An_Unknown_Name() {
        var catalog = new InMemoryInsightSourceCatalog(new Dictionary<string, SourceConfig>());

        Assert.Null(await catalog.ResolveAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task Lists_Registered_Names() {
        var config  = new SourceConfig("repository", new Dictionary<string, object?>());
        var catalog = new InMemoryInsightSourceCatalog(
            new Dictionary<string, SourceConfig> { ["customers"] = config, ["orders"] = config });

        var names = await catalog.ListNamesAsync(CancellationToken.None);

        Assert.Equal(2, names.Count);
        Assert.Contains("customers", names);
        Assert.Contains("orders", names);
    }
}
