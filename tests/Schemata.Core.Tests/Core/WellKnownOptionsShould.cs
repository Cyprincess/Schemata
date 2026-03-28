using System;
using Xunit;

namespace Schemata.Core.Tests.Core;

public class WellKnownOptionsShould
{
    [Fact]
    public void Map_RegisterEndpoint() {
        var options = new WellKnownOptions();

        options.Map("test", () => "ok");

        Assert.Single(options.Endpoints);
        Assert.True(options.Endpoints.ContainsKey("test"));
    }

    [Fact]
    public void Map_DuplicateSuffix_LastWins() {
        var      options = new WellKnownOptions();
        Delegate first   = () => "first";
        Delegate second  = () => "second";

        options.Map("test", first);
        options.Map("test", second);

        Assert.Single(options.Endpoints);
        Assert.Same(second, options.Endpoints["test"]);
    }

    [Fact]
    public void Map_TrimSlashes() {
        var options = new WellKnownOptions();

        options.Map("/test/", () => "ok");

        Assert.True(options.Endpoints.ContainsKey("test"));
    }

    [Fact]
    public void Map_CaseInsensitive() {
        var      options = new WellKnownOptions();
        Delegate handler = () => "ok";

        options.Map("Test", handler);

        Assert.True(options.Endpoints.ContainsKey("test"));
        Assert.True(options.Endpoints.ContainsKey("TEST"));
    }

    [Fact]
    public void Endpoints_EmptyByDefault() {
        var options = new WellKnownOptions();

        Assert.Empty(options.Endpoints);
    }
}
