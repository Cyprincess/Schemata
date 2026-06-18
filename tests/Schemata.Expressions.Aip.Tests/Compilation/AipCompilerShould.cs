using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Aip.Tests.Compilation;

public class AipCompilerShould
{
    private readonly AipCompiler _compiler = new();

    [Fact]
    public void Compile_ComparisonAgainstEntityProperty_ReturnsPredicate() {
        var tree       = _compiler.Parse("age = 18");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Age  = 18 }));
        Assert.False(func(new() { Age = 19 }));
    }

    [Fact]
    public void Compile_SnakeCaseField_BindsPascalCaseProperty() {
        var tree       = _compiler.Parse("full_name = 'Alice'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_ExactVariableAndSnakeCaseField_BindsParameterAndPascalCaseProperty() {
        var tree       = _compiler.Parse("studentProfile.full_name = 'Alice'");
        var expression = _compiler.Compile<StudentProfile, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_SnakeCaseVariable_DoesNotBindCamelCaseParameter() {
        var tree = _compiler.Parse("student_profile.full_name = 'Alice'");

        Assert.Throws<ParseException>(() => _compiler.Compile<StudentProfile, bool>(tree));
    }

    [Fact]
    public void Compile_BareUnknownTerm_Throws() {
        // A bare term that names no field must fail rather than compile to a vacuous match-all.
        var tree = _compiler.Parse("nonexistent");

        Assert.Throws<ParseException>(() => _compiler.Compile<Student, bool>(tree));
    }

    [Fact]
    public void Compile_HasString_UsesContains() {
        var tree       = _compiler.Parse("full_name : 'lic'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_HasList_UsesContains() {
        var tree       = _compiler.Parse("tags : 'red'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Tags  = ["red", "blue"] }));
        Assert.False(func(new() { Tags = ["green"] }));
    }

    [Fact]
    public void Compile_PresenceOnList_RequiresAtLeastOneElement() {
        var tree       = _compiler.Parse("tags : *");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Tags  = ["red"] }));
        Assert.False(func(new() { Tags = [] }));
    }

    [Fact]
    public void Compile_HasDictionary_UsesContainsKey() {
        var tree       = _compiler.Parse("metadata : 'schedule'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Metadata  = new() { ["schedule"] = "daily" } }));
        Assert.False(func(new() { Metadata = new() { ["owner"]    = "ops" } }));
    }

    [Fact]
    public void Compile_CustomFunction_BindsAtCompileTime() {
        var options = new ExpressionCompileOptions();
        options.Functions["startsWith"]
            = new(args => Expression.Call(args[0], nameof(string.StartsWith), null, args[1]));

        var tree       = _compiler.Parse("startsWith(full_name, 'Al')");
        var expression = _compiler.Compile<Student, bool>(tree, options);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_CustomFunctionCacheKey_UsesFunctionIdentity() {
        var first = new ExpressionCompileOptions();
        first.Functions["matches"] = new(args => Expression.Call(args[0], nameof(string.StartsWith), null, args[1]));
        var second = new ExpressionCompileOptions();
        second.Functions["matches"] = new(args => Expression.Call(args[0], nameof(string.EndsWith), null, args[1]));

        var tree       = _compiler.Parse("matches(full_name, 'ice')");
        var startsWith = _compiler.Compile<Student, bool>(tree, first).Compile();
        var endsWith   = _compiler.Compile<Student, bool>(tree, second).Compile();

        Assert.False(startsWith(new() { FullName = "Alice" }));
        Assert.True(endsWith(new() { FullName    = "Alice" }));
    }

    [Fact]
    public void Compile_SameTree_ReturnsCachedExpressionInstance() {
        var tree   = _compiler.Parse("age = 18");
        var first  = _compiler.Compile<Student, bool>(tree);
        var second = _compiler.Compile<Student, bool>(tree);

        Assert.Same(first, second);
    }

    [Fact]
    public void Compile_DistinctSources_ReturnSeparateExpressionInstances() {
        var first  = _compiler.Compile<Student, bool>(_compiler.Parse("age = 18"));
        var second = _compiler.Compile<Student, bool>(_compiler.Parse("age = 19"));

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Compile_TimestampBuiltIn_ReturnsDateTimeConstant() {
        var tree       = _compiler.Parse("start_time > timestamp('2024-01-02T03:04:05Z')");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { StartTime  = DateTime.Parse("2024-01-02T03:04:06Z").ToUniversalTime() }));
        Assert.False(func(new() { StartTime = DateTime.Parse("2024-01-02T03:04:04Z").ToUniversalTime() }));
    }

    [Fact]
    public void Compile_DurationBuiltIn_ReturnsTimeSpanConstant() {
        var tree       = _compiler.Parse("wait > duration('4h0m0s')");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Wait  = TimeSpan.FromHours(5) }));
        Assert.False(func(new() { Wait = TimeSpan.FromHours(3) }));
    }

    [Fact]
    public void Compile_RfcTimestampLiteral_AgainstDateTimeOffsetField() {
        var tree       = _compiler.Parse("created_at > '2024-01-02T03:04:05Z'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { CreatedAt  = DateTimeOffset.Parse("2024-01-02T03:04:06Z") }));
        Assert.False(func(new() { CreatedAt = DateTimeOffset.Parse("2024-01-02T03:04:04Z") }));
    }

    [Fact]
    public void Compile_DecimalSecondsDuration_AgainstTimeSpanField() {
        var tree       = _compiler.Parse("wait > 1.2s");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Wait  = TimeSpan.FromSeconds(2) }));
        Assert.False(func(new() { Wait = TimeSpan.FromSeconds(1) }));
    }

    [Fact]
    public void Compile_RepeatedHasWithNestedField_TranslatesToEnumerableAny() {
        var tree       = _compiler.Parse("pets.age:18");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Pets  = [new() { Age = 18 }] }));
        Assert.False(func(new() { Pets = [new() { Age = 17 }] }));
    }

    [Fact]
    public void Compile_NullMemberChain_SkipsAsNonMatch() {
        var tree       = _compiler.Parse("advisor.full_name = 'Alice'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.False(func(new() { Advisor = null }));
        Assert.True(func(new() { Advisor  = new() { FullName = "Alice" } }));
    }

    [Fact]
    public void Compile_NullMemberChain_ValueTypeLeaf_SkipsAsNonMatchForBothEqAndNeq() {
        var equalTree    = _compiler.Parse("advisor.age = 0");
        var notEqualTree = _compiler.Parse("advisor.age != 0");
        var equalFunc    = _compiler.Compile<Student, bool>(equalTree).Compile();
        var notEqualFunc = _compiler.Compile<Student, bool>(notEqualTree).Compile();

        // Null receiver: AIP skip-as-nonmatch requires BOTH = and != to be false, even when
        // the value-type leaf's default (0) would coincidentally satisfy = against literal 0.
        Assert.False(equalFunc(new() { Advisor    = null }));
        Assert.False(notEqualFunc(new() { Advisor = null }));

        // Present receiver: ordinary comparison semantics apply.
        Assert.True(equalFunc(new() { Advisor     = new() { Age = 0 } }));
        Assert.False(notEqualFunc(new() { Advisor = new() { Age = 0 } }));
        Assert.False(equalFunc(new() { Advisor    = new() { Age = 5 } }));
        Assert.True(notEqualFunc(new() { Advisor  = new() { Age = 5 } }));
    }

    #region Nested type: Student

    private sealed class Student
    {
        public string?                    FullName  { get; set; }
        public int                        Age       { get; set; }
        public DateTime                   StartTime { get; set; }
        public DateTimeOffset             CreatedAt { get; set; }
        public TimeSpan                   Wait      { get; set; }
        public List<string>               Tags      { get; set; } = [];
        public List<Pet>                  Pets      { get; set; } = [];
        public Dictionary<string, string> Metadata  { get; set; } = [];
        public StudentProfile?            Advisor   { get; set; }
    }

    private sealed class Pet
    {
        public int Age { get; set; }
    }

    #endregion

    #region Nested type: StudentProfile

    private sealed class StudentProfile
    {
        public string? FullName { get; set; }

        public int Age { get; set; }
    }

    #endregion
}
