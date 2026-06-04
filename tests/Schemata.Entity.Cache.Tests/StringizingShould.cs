using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Threading;
using Schemata.Entity.Cache.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Cache.Tests;

public class StringizingShould
{
    [Fact]
    public void ToString_Lambda_RenamesParameterDeterministically() {
        Expression<Func<Student, bool>> expr = s => s.Age > 18;

        var result = Stringizing.ToString(expr);

        Assert.Equal("(_p0) => (_p0.Age > 18)", result);
    }

    [Fact]
    public void ToString_BinaryEqual_ProducesExpectedString() {
        Expression<Func<Student, bool>> expr = s => s.FullName == "Alice";

        var result = Stringizing.ToString(expr);

        Assert.Equal("(_p0) => (_p0.FullName == \"Alice\")", result);
    }

    [Fact]
    public void ToString_AndAlso_ProducesExpectedString() {
        Expression<Func<Student, bool>> expr = s => s.Age > 18 && s.FullName == "Bob";

        var result = Stringizing.ToString(expr);

        Assert.Equal("(_p0) => ((_p0.Age > 18) && (_p0.FullName == \"Bob\"))", result);
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

        Assert.Equal("(_p0) => !(_p0 > 5)", result);
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

        Assert.Equal("(_p0) => _p0.FullName.Contains:1(\"Al\")", result);
    }

    [Fact]
    public void ToString_DifferentlyNamedEquivalentLambdas_ProduceSameString() {
        Expression<Func<Student, bool>> alpha = student => student.Age > 18 && student.FullName == "Alice";
        Expression<Func<Student, bool>> beta  = x => x.Age > 18 && x.FullName == "Alice";

        var alphaResult = Stringizing.ToString(alpha);
        var betaResult  = Stringizing.ToString(beta);

        Assert.Equal(alphaResult, betaResult);
    }

    [Fact]
    public void ToString_MultipleParameters_AssignsSequentialAliases() {
        Expression<Func<Student, Student, bool>> expr = (first, second) => first.Age > second.Age;

        var result = Stringizing.ToString(expr);

        Assert.Equal("(_p0, _p1) => (_p0.Age > _p1.Age)", result);
    }

    [Fact]
    public void ToString_DateTimeConstant_ProducesSameStringAcrossCultures() {
        var dt   = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        var expr = (Expression)Expression.Constant(dt);

        string en, de;
        using (new CultureSwitch("en-US")) {
            en = Stringizing.ToString(expr);
        }

        using (new CultureSwitch("de-DE")) {
            de = Stringizing.ToString(expr);
        }

        Assert.Equal(en, de);
    }

    [Fact]
    public void ToString_DecimalConstant_IsCultureInvariant() {
        var expr = (Expression)Expression.Constant(12345.678m);

        string en, de;
        using (new CultureSwitch("en-US")) {
            en = Stringizing.ToString(expr);
        }

        using (new CultureSwitch("de-DE")) {
            de = Stringizing.ToString(expr);
        }

        Assert.Equal(en, de);
    }

    [Fact]
    public void ToString_DoubleConstant_IsCultureInvariant() {
        var expr = (Expression)Expression.Constant(12345.678d);

        string en, de;
        using (new CultureSwitch("en-US")) {
            en = Stringizing.ToString(expr);
        }

        using (new CultureSwitch("de-DE")) {
            de = Stringizing.ToString(expr);
        }

        Assert.Equal(en, de);
    }

    [Fact]
    public void ToString_ConvertToLong_DiffersFromConvertToDouble() {
        Expression<Func<Student, long>>   toLong   = s => s.Age;
        Expression<Func<Student, double>> toDouble = s => s.Age;

        var left  = Stringizing.ToString(toLong);
        var right = Stringizing.ToString(toDouble);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ToString_StaticMethodCall_IsNotRenderedAsInstanceCall() {
        Expression<Func<Student, bool>> expr = s => string.IsNullOrEmpty(s.FullName);

        var result = Stringizing.ToString(expr);

        Assert.Contains("IsNullOrEmpty", result);
        Assert.Contains("String", result);
        Assert.DoesNotContain(".FullName.IsNullOrEmpty", result);
    }

    [Fact]
    public void ToString_DifferentArityMethodCalls_ProduceDistinctStrings() {
        Expression<Func<Student, bool>> one = s => s.FullName!.Contains("Al");
        Expression<Func<Student, bool>> two = s => s.FullName!.Contains("Al", StringComparison.Ordinal);

        var left  = Stringizing.ToString(one);
        var right = Stringizing.ToString(two);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ToString_ConditionalExpression_IncludesQuestionColon() {
        Expression<Func<Student, string>> expr = s => s.Age > 18 ? "adult" : "minor";

        var result = Stringizing.ToString(expr);

        Assert.Contains("?", result);
        Assert.Contains(":", result);
        Assert.Contains("adult", result);
        Assert.Contains("minor", result);
    }

    [Fact]
    public void ToString_DifferentConditionalBranches_ProduceDistinctStrings() {
        Expression<Func<Student, string>> a = s => s.Age > 18 ? "adult" : "minor";
        Expression<Func<Student, string>> b = s => s.Age > 18 ? "senior" : "minor";

        Assert.NotEqual(Stringizing.ToString(a), Stringizing.ToString(b));
    }

    [Fact]
    public void ToString_NewExpression_IncludesConstructedTypeName() {
        Expression<Func<Guid, Grade>>   grade   = uid => new(uid, 1);
        Expression<Func<Guid, Student>> student = uid => new() { Uid = uid };

        var left  = Stringizing.ToString(grade);
        var right = Stringizing.ToString(student);

        Assert.NotEqual(left, right);
        Assert.Contains("Grade", left);
        Assert.Contains("Student", right);
    }

    [Fact]
    public void ToString_MemberInit_DistinguishesBindingsByName() {
        Expression<Func<Student, Student>> byId   = s => new() { Uid      = s.Uid };
        Expression<Func<Student, Student>> byName = s => new() { FullName = s.FullName };

        var left  = Stringizing.ToString(byId);
        var right = Stringizing.ToString(byName);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ToString_TypeIsExpression_DistinguishesTargetTypes() {
        Expression<Func<object, bool>> isStudent = o => o is Student;
        Expression<Func<object, bool>> isGrade   = o => o is Grade;

        var left  = Stringizing.ToString(isStudent);
        var right = Stringizing.ToString(isGrade);

        Assert.NotEqual(left, right);
    }

    #region Nested type: CultureSwitch

    private sealed class CultureSwitch : IDisposable
    {
        private readonly CultureInfo _culture;
        private readonly CultureInfo _ui;

        public CultureSwitch(string name) {
            _culture = Thread.CurrentThread.CurrentCulture;
            _ui      = Thread.CurrentThread.CurrentUICulture;
            var target = new CultureInfo(name);
            Thread.CurrentThread.CurrentCulture   = target;
            Thread.CurrentThread.CurrentUICulture = target;
        }

        #region IDisposable Members

        public void Dispose() {
            Thread.CurrentThread.CurrentCulture   = _culture;
            Thread.CurrentThread.CurrentUICulture = _ui;
        }

        #endregion
    }

    #endregion
}
