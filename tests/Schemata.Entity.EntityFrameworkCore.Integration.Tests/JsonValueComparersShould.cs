using System.Collections.Generic;
using Schemata.Entity.EntityFrameworkCore.Conversions;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class JsonValueComparersShould
{
    [Fact]
    public void EqualContentInDifferentInsertionOrder_EqualsAndHashesEqually() {
        var a = new Dictionary<string, string> { ["x"] = "1", ["y"] = "2" };
        var b = new Dictionary<string, string> { ["y"] = "2", ["x"] = "1" };

        var comparer = JsonValueComparers.DictionaryStringString;

        Assert.True(comparer.Equals(a, b));
        Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
    }

    [Fact]
    public void UnchangedSnapshot_EqualsAndHashesEqually() {
        var value = new Dictionary<string, string> { ["x"] = "1", ["y"] = "2" };

        var comparer = JsonValueComparers.DictionaryStringString;
        var snapshot = comparer.Snapshot(value);

        Assert.NotNull(snapshot);
        Assert.True(comparer.Equals(value, snapshot));
        Assert.Equal(comparer.GetHashCode(value), comparer.GetHashCode(snapshot));
    }

    [Fact]
    public void InPlaceValueChange_IsDetectedAgainstSnapshot() {
        var value = new Dictionary<string, string> { ["x"] = "1" };

        var comparer = JsonValueComparers.DictionaryStringString;
        var snapshot = comparer.Snapshot(value);

        value["x"] = "2";

        Assert.False(comparer.Equals(snapshot, value));
    }
}
