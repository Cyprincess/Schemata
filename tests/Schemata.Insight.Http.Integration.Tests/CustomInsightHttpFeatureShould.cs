using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Schemata.Core;
using Schemata.Insight.Foundation.Features;
using Schemata.Insight.Http.Features;
using Schemata.Insight.Skeleton.Entities;
using Xunit;

namespace Schemata.Insight.Http.Integration.Tests;

/// <summary>
///     Proves <c>MapHttp()</c> installs the HTTP feature closed on the source entity the Insight
///     entry selected.
/// </summary>
public sealed class CustomInsightHttpFeatureShould
{
    [Fact]
    public void MapHttp_Closes_The_Transport_On_The_Selected_Source() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseInsight<TransportSource>().MapHttp();

        Assert.True(builder.HasFeature<SchemataInsightHttpFeature<TransportSource>>());
        Assert.True(builder.HasFeature<SchemataInsightFeature>());
        Assert.False(builder.HasFeature<SchemataInsightHttpFeature<SchemataInsightSource>>());
    }

    [Fact]
    public void MapHttp_For_The_Default_Entry_Closes_The_Transport_On_The_Built_In_Source() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseInsight().MapHttp();

        Assert.True(builder.HasFeature<SchemataInsightHttpFeature<SchemataInsightSource>>());
    }

    private sealed class TransportSource : SchemataInsightSource;
}
