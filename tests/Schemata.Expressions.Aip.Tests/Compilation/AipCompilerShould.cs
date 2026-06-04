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

    #region Nested type: Student

    private sealed class Student
    {
        public string?                    FullName  { get; set; }
        public int                        Age       { get; set; }
        public DateTime                   StartTime { get; set; }
        public TimeSpan                   Wait      { get; set; }
        public List<string>               Tags      { get; set; } = [];
        public Dictionary<string, string> Metadata  { get; set; } = [];
    }

    #endregion

    #region Nested type: StudentProfile

    private sealed class StudentProfile
    {
        public string? FullName { get; set; }
    }

    #endregion
}
