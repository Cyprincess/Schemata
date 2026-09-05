using Schemata.Common;
using Schemata.Insight.Skeleton.Entities;
using Xunit;

namespace Schemata.Insight.Tests;

public class InsightSourceNamingShould
{
    [Fact]
    public void Read_The_Resource_Identity_From_The_Pattern() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataInsightSource>();

        Assert.Equal("InsightSource", descriptor.Singular);
        Assert.Equal("InsightSources", descriptor.Plural);
        Assert.Equal("insightSources", descriptor.Collection);
    }

    [Fact]
    public void Resolve_The_Canonical_Name_Of_A_Persisted_Source() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataInsightSource>();

        var resolved = descriptor.Resolve(new SchemataInsightSource { Name = "orders" });

        Assert.Equal("insightSources/orders", resolved);
    }

    [Fact]
    public void Parse_A_Persisted_Source_Name_Back_To_Its_Leaf() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataInsightSource>();

        var parsed = descriptor.ParseCanonicalName("insightSources/orders");

        Assert.NotNull(parsed);
        Assert.Equal("orders", parsed.Value.LeafName);
    }
}
