using System;
using System.Globalization;
using System.Linq;
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

        Assert.NotNull(result);
        Assert.Contains("_p0:", result);
        Assert.Contains(":Age > i4:18", result);
    }

    [Fact]
    public void ToString_EqualExpressionWithStringLiteral_RendersQuotedLiteral() {
        Expression<Func<Student, bool>> expr = s => s.FullName == "Alice";

        var result = Stringizing.ToString(expr);

        Assert.NotNull(result);
        Assert.Contains(":FullName == \"Alice\"", result);
    }

    [Fact]
    public void ToString_AndAlsoConjunction_RendersParenthesizedAndExpression() {
        Expression<Func<Student, bool>> expr = s => s.Age > 18 && s.FullName == "Bob";

        var result = Stringizing.ToString(expr);

        Assert.NotNull(result);
        Assert.Contains(" && ", result);
        Assert.Contains("\"Bob\"", result);
        Assert.Contains("i4:18", result);
    }

    [Fact]
    public void ToString_NullConstant_ProducesNull() {
        Expression<Func<Student, bool>> expr = s => s.FullName == null;

        var result = Stringizing.ToString(expr);

        Assert.NotNull(result);
        Assert.Contains("null", result);
    }

    [Fact]
    public void ToString_NotExpression_ProducesNegation() {
        Expression<Func<int, bool>> expr = x => !(x > 5);

        var result = Stringizing.ToString(expr);

        Assert.NotNull(result);
        Assert.Contains("!(", result);
    }

    [Fact]
    public void ToString_EquivalentExpressions_ProduceSameString() {
        Expression<Func<Student, bool>> expr1 = s => s.Age > 18;
        Expression<Func<Student, bool>> expr2 = s => s.Age > 18;

        Assert.Equal(Stringizing.ToString(expr1), Stringizing.ToString(expr2));
    }

    [Fact]
    public void ToString_DifferentExpressions_ProduceDifferentStrings() {
        Expression<Func<Student, bool>> expr1 = s => s.Age > 18;
        Expression<Func<Student, bool>> expr2 = s => s.Age < 18;

        Assert.NotEqual(Stringizing.ToString(expr1), Stringizing.ToString(expr2));
    }


    [Fact]
    public void ToString_DifferentlyNamedEquivalentLambdas_ProduceSameString() {
        Expression<Func<Student, bool>> alpha = student => student.Age > 18 && student.FullName == "Alice";
        Expression<Func<Student, bool>> beta  = x => x.Age > 18 && x.FullName == "Alice";

        Assert.Equal(Stringizing.ToString(alpha), Stringizing.ToString(beta));
    }

    [Fact]
    public void ToString_MultipleParameters_AssignsSequentialAliases() {
        Expression<Func<Student, Student, bool>> expr = (first, second) => first.Age > second.Age;

        var result = Stringizing.ToString(expr);

        Assert.NotNull(result);
        Assert.Contains("_p0:", result);
        Assert.Contains("_p1:", result);
    }

    [Fact]
    public void ToString_DifferentParameterTypes_ProduceDistinctAliases() {
        Expression<Func<Student, bool>> byStudent = s => s.Age > 18;
        Expression<Func<Grade, bool>>   byGrade   = g => g.Level > 18;

        Assert.NotEqual(Stringizing.ToString(byStudent), Stringizing.ToString(byGrade));
    }

    [Fact]
    public void ToString_DateTimeConstant_ProducesSameStringAcrossCultures() {
        var dt   = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        var expr = (Expression)Expression.Constant(dt);

        string en, de;
        using (new CultureSwitch("en-US")) {
            en = Stringizing.ToString(expr)!;
        }

        using (new CultureSwitch("de-DE")) {
            de = Stringizing.ToString(expr)!;
        }

        Assert.Equal(en, de);
        Assert.StartsWith("dt", en);
    }

    [Fact]
    public void ToString_DecimalConstant_IsCultureInvariant() {
        var expr = (Expression)Expression.Constant(12345.678m);

        string en, de;
        using (new CultureSwitch("en-US")) {
            en = Stringizing.ToString(expr)!;
        }

        using (new CultureSwitch("de-DE")) {
            de = Stringizing.ToString(expr)!;
        }

        Assert.Equal(en, de);
        Assert.StartsWith("d:", en);
    }

    [Fact]
    public void ToString_DoubleConstant_IsCultureInvariant() {
        var expr = (Expression)Expression.Constant(12345.678d);

        string en, de;
        using (new CultureSwitch("en-US")) {
            en = Stringizing.ToString(expr)!;
        }

        using (new CultureSwitch("de-DE")) {
            de = Stringizing.ToString(expr)!;
        }

        Assert.Equal(en, de);
        Assert.StartsWith("f8:", en);
    }

    [Fact]
    public void ToString_ConvertToLong_DiffersFromConvertToDouble() {
        Expression<Func<Student, long>>   toLong   = s => s.Age;
        Expression<Func<Student, double>> toDouble = s => s.Age;

        Assert.NotEqual(Stringizing.ToString(toLong), Stringizing.ToString(toDouble));
    }

    [Fact]
    public void ToString_StaticMethodCall_IsNotRenderedAsInstanceCall() {
        Expression<Func<Student, bool>> expr = s => string.IsNullOrEmpty(s.FullName);

        var result = Stringizing.ToString(expr);

        Assert.NotNull(result);
        Assert.Contains("IsNullOrEmpty", result);
        Assert.DoesNotContain(".FullName.IsNullOrEmpty", result);
    }

    [Fact]
    public void ToString_DifferentParameterTypeLists_ProduceDistinctMethods() {
        Expression<Func<Student, bool>> one = s => s.FullName!.Contains("Al");
        Expression<Func<Student, bool>> two = s => s.FullName!.Contains("Al", StringComparison.Ordinal);

        Assert.NotEqual(Stringizing.ToString(one), Stringizing.ToString(two));
    }

    [Fact]
    public void ToString_ConditionalExpression_IncludesQuestionColon() {
        Expression<Func<Student, string>> expr = s => s.Age > 18 ? "adult" : "minor";

        var result = Stringizing.ToString(expr);

        Assert.NotNull(result);
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

    [Fact]
    public void ToString_ControlCharacterAndLiteralEscape_ProduceDifferentKeys() {
        Expression<Func<Student, bool>> control = s => s.FullName == "a\u001eb";
        Expression<Func<Student, bool>> escaped = s => s.FullName == "a\\u001eb";

        var controlKey = Stringizing.ToString(control);
        var escapedKey = Stringizing.ToString(escaped);

        Assert.NotNull(controlKey);
        Assert.NotNull(escapedKey);
        Assert.NotEqual(controlKey, escapedKey);
    }

    [Fact]
    public void ToString_NumericConstantsOfDifferentTypes_AreTypeTagged() {
        var intConstant    = (Expression)Expression.Constant(5);
        var longConstant   = (Expression)Expression.Constant(5L);
        var doubleConstant = (Expression)Expression.Constant(5d);

        var intResult    = Stringizing.ToString(intConstant);
        var longResult   = Stringizing.ToString(longConstant);
        var doubleResult = Stringizing.ToString(doubleConstant);

        Assert.NotEqual(intResult, longResult);
        Assert.NotEqual(intResult, doubleResult);
        Assert.Equal("i4:5", intResult);
        Assert.Equal("i8:5", longResult);
    }

    [Fact]
    public void ToString_CapturedArrayContents_ProduceDistinctStrings() {
        var left  = (Expression)Expression.Constant(new[] { 1, 2 });
        var right = (Expression)Expression.Constant(new[] { 1, 3 });
        var again = (Expression)Expression.Constant(new[] { 1, 2 });

        Assert.NotEqual(Stringizing.ToString(left), Stringizing.ToString(right));
        Assert.Equal(Stringizing.ToString(left), Stringizing.ToString(again));
    }

    [Fact]
    public void ToString_EmptyArraysOfDifferentElementTypes_ProduceDistinctStrings() {
        var ints   = (Expression)Expression.Constant(Array.Empty<int>());
        var strings = (Expression)Expression.Constant(Array.Empty<string>());

        Assert.NotEqual(Stringizing.ToString(ints), Stringizing.ToString(strings));
    }

    [Fact]
    public void ToString_UnknownReferenceConstant_ReturnsNull() {
        var expr = (Expression)Expression.Constant(new object());

        Assert.Null(Stringizing.ToString(expr));
    }

    [Fact]
    public void ToString_QueryableConstant_ReturnsNullWithoutInstanceIdentity() {
        var expr = (Expression)Expression.Constant(Array.Empty<Student>().AsQueryable());

        Assert.Null(Stringizing.ToString(expr));
    }

    [Fact]
    public void ToString_NewArrayExpression_ReturnsNullInsteadOfDroppingSemantics() {
        var expr = Expression.NewArrayInit(typeof(int), Expression.Constant(1));

        Assert.Null(Stringizing.ToString(expr));
    }

    [Fact]
    public void ToString_ClosedMethodCall_RendersIdentityWithoutInvoking() {
        Expression<Func<Student, bool>> expr = s => s.Age > BumpCounter();

        var result = Stringizing.ToString(expr);

        Assert.NotNull(result);
        Assert.Equal(0, ClosedCallCount);
        Assert.Contains("BumpCounter", result);
    }


    [Fact]
    public void ToString_ClosureFieldValues_FoldToDistinctConstants() {
        var low  = 10;
        var high = 11;
        Expression<Func<Student, bool>> l = s => s.Age > low;
        Expression<Func<Student, bool>> h = s => s.Age > high;

        var left  = Stringizing.ToString(l);
        var right = Stringizing.ToString(h);

        Assert.NotEqual(left, right);
        Assert.Contains("i4:10", left);
    }

    [Fact]
    public void ToStructure_QueryableConstants_AreOpaqueMarkersWithoutInstanceIdentity() {
        var first  = (Expression)Expression.Constant(Array.Empty<Student>().AsQueryable());
        var second = (Expression)Expression.Constant(Array.Empty<Student>().AsQueryable());

        var left  = Stringizing.ToStructure(first);
        var right = Stringizing.ToStructure(second);

        Assert.NotNull(left);
        Assert.Equal(left, right);
        Assert.Contains("root:", left);
    }

    [Fact]
    public void ToStructure_ProviderRootExtension_RendersSingleOpaqueMarker() {
        var result = Stringizing.ToStructure(new ExtensionNode());

        Assert.NotNull(result);
        Assert.Contains("xroot:", result);
    }

    [Fact]
    public void ToStructure_SecondExtensionNode_ReturnsNull() {
        var tree = Expression.Equal(new ExtensionNode(), new ExtensionNode());

        Assert.Null(Stringizing.ToStructure(tree));
    }

    [Fact]
    public void ToString_ExtensionNode_ReturnsNullInsteadOfDroppingSemantics() {
        Assert.Null(Stringizing.ToString(new ExtensionNode()));
    }
    private static int ClosedCallCount;

    private static int BumpCounter() {
        ClosedCallCount++;
        return ClosedCallCount;
    }

    private sealed class ExtensionNode : Expression
    {
        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type => typeof(object);
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
