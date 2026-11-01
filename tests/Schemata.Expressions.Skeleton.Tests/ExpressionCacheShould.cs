using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Schemata.Expressions.Skeleton.Tests;

public class ExpressionCacheShould
{
    [Fact]
    public void ExpressionCacheKey_ForEqualPartsFromDistinctInstances_IsEqualWithEqualHash() {
        var firstSource  = Copy("age = 18");
        var secondSource = Copy("age = 18");
        Assert.NotSame(firstSource, secondSource);

        var first = ExpressionCacheKey.Create(
            Copy("aip"), firstSource, typeof(Student), typeof(bool), Copy("functions:none"));
        var second = ExpressionCacheKey.Create(
            Copy("aip"), secondSource, typeof(Student), typeof(bool), Copy("functions:none"));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Theory]
    [MemberData(nameof(DifferingComponents))]
    public void ExpressionCacheKey_ForDifferingComponent_IsNotEqual(
        string language,
        string  source,
        Type    contextType,
        Type    resultType,
        string  options
    ) {
        var baseline = ExpressionCacheKey.Create("aip", "age = 18", typeof(Student), typeof(bool), "functions:none");
        var variant  = ExpressionCacheKey.Create(language, source, contextType, resultType, options);

        Assert.NotEqual(baseline, variant);
    }

    public static IEnumerable<object[]> DifferingComponents() {
        yield return ["cel", "age = 18", typeof(Student), typeof(bool), "functions:none"];
        yield return ["aip", "age = 19", typeof(Student), typeof(bool), "functions:none"];
        yield return ["aip", "age = 18", typeof(Teacher), typeof(bool), "functions:none"];
        yield return ["aip", "age = 18", typeof(Student), typeof(int), "functions:none"];
        yield return ["aip", "age = 18", typeof(Student), typeof(bool), "functions:all"];
    }

    private static string Copy(string value) {
        var builder = new StringBuilder(value.Length + 1);
        builder.Append(value).Append(' ');
        return builder.ToString(0, value.Length);
    }

    #region Nested types: Student, Teacher

    private sealed class Student
    {
        public int Age { get; set; }
    }

    private sealed class Teacher
    {
        public string? Name { get; set; }
    }

    #endregion
}
