using System;
using System.Linq.Expressions;
using Schemata.Entity.Cache.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Cache.Tests;

public class StringizingShould
{
    [Fact]
    public void ToString_Lambda_ProducesExpectedString() {
        Expression<Func<Student, bool>> expr = s => s.Age > 18;

        var result = Stringizing.ToString(expr);

        Assert.Equal("(s) => (s.Age > 18)", result);
    }

    [Fact]
    public void ToString_BinaryEqual_ProducesExpectedString() {
        Expression<Func<Student, bool>> expr = s => s.FullName == "Alice";

        var result = Stringizing.ToString(expr);

        Assert.Equal("(s) => (s.FullName == \"Alice\")", result);
    }

    [Fact]
    public void ToString_AndAlso_ProducesExpectedString() {
        Expression<Func<Student, bool>> expr = s => s.Age > 18 && s.FullName == "Bob";

        var result = Stringizing.ToString(expr);

        Assert.Equal("(s) => ((s.Age > 18) && (s.FullName == \"Bob\"))", result);
    }

    [Fact]
    public void ToString_NullConstant_ProducesNull() {
        Expression<Func<Student, bool>> expr = s => s.FullName == null;

        var result = Stringizing.ToString(expr);

        Assert.Contains("null", result);
    }

    [Fact]
    public void ToString_NotExpression_ProducesNegation() {
        Expression<Func<int, bool>> expr = x => !(x > 5);

        var result = Stringizing.ToString(expr);

        Assert.Equal("(x) => !(x > 5)", result);
    }

    [Fact]
    public void ToString_EquivalentExpressions_ProduceSameString() {
        Expression<Func<Student, bool>> expr1 = s => s.Age > 18;
        Expression<Func<Student, bool>> expr2 = s => s.Age > 18;

        var result1 = Stringizing.ToString(expr1);
        var result2 = Stringizing.ToString(expr2);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void ToString_DifferentExpressions_ProduceDifferentStrings() {
        Expression<Func<Student, bool>> expr1 = s => s.Age > 18;
        Expression<Func<Student, bool>> expr2 = s => s.Age < 18;

        var result1 = Stringizing.ToString(expr1);
        var result2 = Stringizing.ToString(expr2);

        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void ToString_MethodCall_ProducesExpectedString() {
        Expression<Func<Student, bool>> expr = s => s.FullName!.Contains("Al");

        var result = Stringizing.ToString(expr);

        Assert.Equal("(s) => s.FullName.Contains(\"Al\")", result);
    }
}
