using Schemata.Common;
using Schemata.Insight.Skeleton.Entities;
using Xunit;

namespace Schemata.Insight.Tests;

public class InsightSourceNamingShould
{
    [Fact]
    public void ReadTheResourceIdentityFromThePattern() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataInsightSource>();

        Assert.Equal("InsightSource", descriptor.Singular);
        Assert.Equal("InsightSources", descriptor.Plural);
        Assert.Equal("insightSources", descriptor.Collection);
    }

    [Fact]
    public void ResolveTheCanonicalNameOfAPersistedSource() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataInsightSource>();

        var resolved = descriptor.Resolve(new SchemataInsightSource { Name = "orders" });

        Assert.Equal("insightSources/orders", resolved);
    }

    [Fact]
    public void ParseAPersistedSourceNameBackToItsLeaf() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataInsightSource>();

        var parsed = descriptor.ParseCanonicalName("insightSources/orders");

        Assert.NotNull(parsed);
        Assert.Equal("orders", parsed.Value.LeafName);
    }
}
