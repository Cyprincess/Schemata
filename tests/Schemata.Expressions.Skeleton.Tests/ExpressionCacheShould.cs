using Xunit;

namespace Schemata.Expressions.Skeleton.Tests;

public class ExpressionCacheShould
{
    [Fact]
    public void LruCache_EvictsLeastRecentlyUsedEntry() {
        var cache = new LruCache<string, int>(2);

        cache.GetOrAdd("a", () => 1);
        cache.GetOrAdd("b", () => 2);
        cache.GetOrAdd("a", () => 10);
        cache.GetOrAdd("c", () => 3);

        Assert.True(cache.TryGet("a", out var a));
        Assert.False(cache.TryGet("b", out var _));
        Assert.True(cache.TryGet("c", out var c));
        Assert.Equal(1, a);
        Assert.Equal(3, c);
    }

    [Fact]
    public void ExpressionCacheKey_ForSameParts_ReturnsSameHash() {
        var first  = ExpressionCacheKey.Create("aip", "age = 18", typeof(Student), typeof(bool), "functions:none");
        var second = ExpressionCacheKey.Create("aip", "age = 18", typeof(Student), typeof(bool), "functions:none");

        Assert.Equal(first, second);
    }

    #region Nested type: Student

    private sealed class Student
    {
        public int Age { get; set; }
    }

    #endregion
}
