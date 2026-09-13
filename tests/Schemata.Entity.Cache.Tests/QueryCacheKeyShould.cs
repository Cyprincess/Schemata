using System;
using System.Collections.Generic;
using System.Globalization;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests;

[Trait("Layer", "Unit")]
public class QueryCacheKeyShould
{
    [Fact]
    public void Create_WithIdenticalInputs_ProducesSameKey() {
        var first = Create("efcore", "Server=s;Database=d", "SELECT 1", [("p", "int", 1)]);
        var second = Create("efcore", "Server=s;Database=d", "SELECT 1", [("p", "int", 1)]);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("source")]
    [InlineData("command")]
    public void Create_WithDifferentComponent_ProducesDifferentKey(string component) {
        var baseArguments = ("efcore", "db", "SELECT 1");
        var altered = component switch {
            "provider" => ("linq2db", "db", "SELECT 1"),
            "source"   => ("efcore", "other", "SELECT 1"),
            _          => ("efcore", "db", "SELECT 2"),
        };

        Assert.NotEqual(Create(baseArguments.Item1, baseArguments.Item2, baseArguments.Item3, [("p", "int", 1)]),
            Create(altered.Item1, altered.Item2, altered.Item3, [("p", "int", 1)]));
    }

    [Fact]
    public void Create_WithDifferentParameterDimensions_ProducesDifferentKeys() {
        var baseline = Create("efcore", "db", "SELECT 1", [("p", "int", 1)]);

        Assert.NotEqual(baseline, Create("efcore", "db", "SELECT 1", [("q", "int", 1)]));
        Assert.NotEqual(baseline, Create("efcore", "db", "SELECT 1", [("p", "bigint", 1)]));
        Assert.NotEqual(baseline, Create("efcore", "db", "SELECT 1", [("p", "int", 2)]));
        Assert.NotEqual(baseline, Create("efcore", "db", "SELECT 1", [("p", "int", 1), ("q", "int", 2)]));
    }

    [Fact]
    public void Create_WithReorderedParameters_ProducesDifferentKey() {
        var forward = Create("efcore", "db", "SELECT 1", [("a", "int", 1), ("b", "int", 2)]);
        var backward = Create("efcore", "db", "SELECT 1", [("b", "int", 2), ("a", "int", 1)]);

        Assert.NotEqual(forward, backward);
    }

    [Fact]
    public void Create_WithScalarArrays_EncodesElementValues() {
        var left = Create("efcore", "db", "SELECT 1", [("p", "int[]", new[] { 1, 2 })]);
        var right = Create("efcore", "db", "SELECT 1", [("p", "int[]", new[] { 1, 3 })]);
        var again = Create("efcore", "db", "SELECT 1", [("p", "int[]", new[] { 1, 2 })]);

        Assert.Equal(left, again);
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Create_WithEmptyArrays_DistinguishesElementTypes() {
        var ints = Create("efcore", "db", "SELECT 1", [("p", "int[]", Array.Empty<int>())]);
        var strings = Create("efcore", "db", "SELECT 1", [("p", "text[]", Array.Empty<string>())]);

        Assert.NotEqual(ints, strings);
    }

    [Fact]
    public void Create_WithUnsupportedValue_ReturnsNull() {
        Assert.Null(Create("efcore", "db", "SELECT 1", [("p", "jsonb", new object())]));
        Assert.Null(Create("efcore", "db", "SELECT 1", [("p", "int[]", new List<int> { 1 })]));
    }

    [Fact]
    public void Create_WithNestedOrNonZeroBasedArrays_ReturnsNull() {
        Assert.Null(Create("efcore", "db", "SELECT 1", [("p", "int[][]", new[] { new[] { 1 } })]));

        var jagged = Array.CreateInstance(typeof(int), [2], [1]);
        Assert.Null(Create("efcore", "db", "SELECT 1", [("p", "int[]", jagged)]));
    }

    [Fact]
    public void Create_WithEnumValue_IsSupportedAndDistinguishesType() {
        var first = Create("efcore", "db", "SELECT 1", [("p", "enum", DayOfWeek.Monday)]);
        var second = Create("efcore", "db", "SELECT 1", [("p", "enum", StringComparison.Ordinal)]);
        var again = Create("efcore", "db", "SELECT 1", [("p", "enum", DayOfWeek.Monday)]);

        Assert.NotNull(first);
        Assert.Equal(first, again);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(null, "db", "SELECT 1")]
    [InlineData("", "db", "SELECT 1")]
    [InlineData("efcore", null, "SELECT 1")]
    [InlineData("efcore", "", "SELECT 1")]
    [InlineData("efcore", "db", null)]
    [InlineData("efcore", "db", "")]
    public void Create_WithEmptyComponent_ReturnsNull(string? provider, string? source, string? command) {
        Assert.Null(Create(provider!, source!, command!, []));
    }

    [Fact]
    public void Create_WithNullParameterValues_IsSupported() {
        var first = Create("efcore", "db", "SELECT 1", [("p", "text", null)]);
        var second = Create("efcore", "db", "SELECT 1", [("p", "text", "value")]);

        Assert.NotNull(first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Create_WithSeparatorCharactersInValues_DoesNotCollide() {
        var embedded = Create("efcore", "db", "SELECT 1", [("a\x1eb", "c", 1)]);
        var split = Create("efcore", "db", "SELECT 1", [("a", "b\x1ec", 1)]);

        Assert.NotEqual(embedded, split);
    }

    [Fact]
    public void Create_WithCultureSensitiveValues_ProducesCultureInvariantKey() {
        var parameters = new List<(string, string, object?)> {
            ("p", "int", -1),
            ("q", "decimal", -12.5m),
            ("r", "double", -3.25d),
        };

        string invariant;
        var original = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariant = Create("efcore", "db", "SELECT 1", parameters)!;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var german = Create("efcore", "db", "SELECT 1", parameters);

            Assert.Equal(invariant, german);
        } finally {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Create_WithCredentialBearingSource_DoesNotExposeRawMaterial() {
        var key = Create("efcore", "Server=s;Database=d;User Id=u;Password=s3cret!", "SELECT 1", []);

        Assert.NotNull(key);
        Assert.DoesNotContain("s3cret", key);
        Assert.DoesNotContain("Password", key);
        Assert.DoesNotContain("SELECT", key);
    }

    [Fact]
    public void Create_WithNullParameterList_ReturnsNull() {
        Assert.Null(Create("efcore", "db", "SELECT 1", null!));
    }

    private static string? Create(
        string provider,
        string source,
        string command,
        IEnumerable<(string Name, string Type, object? Value)> parameters
    ) {
        return QueryCacheKey.Create(provider, source, command, parameters);
    }
}
