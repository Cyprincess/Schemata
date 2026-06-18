using Xunit;

namespace Schemata.Expressions.Skeleton.Tests;

public class ExpressionCacheShould
{
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
