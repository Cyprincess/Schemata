using System;
using Parlot;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class CelCompilerShould
{
    private readonly CelCompiler _compiler = new();

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1 < 2", true)]
    [InlineData("1 > 2", false)]
    [InlineData("full_name == 'Alice'", true)]
    [InlineData("full_name == 'Bob'", false)]
    public void Compile_BasicPredicate_ReturnsExpectedResult(string source, bool expected) {
        var tree       = _compiler.Parse(source);
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.Equal(expected, func(new() { FullName = "Alice" }));
    }

    [Fact]
    public void Compile_HasProperty_ReturnsPresenceCheck() {
        var tree       = _compiler.Parse("has(full_name)");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = null }));
    }

    [Fact]
    public void Compile_ExactVariableAndSnakeCaseMember_BindsVariableAndPascalCaseMember() {
        var tree       = _compiler.Parse("studentProfile.full_name == 'Alice'");
        var expression = _compiler.Compile<StudentProfile, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_SnakeCaseVariable_DoesNotBindCamelCaseParameter() {
        var tree = _compiler.Parse("student_profile.full_name == 'Alice'");

        Assert.Throws<ParseException>(() => _compiler.Compile<StudentProfile, bool>(tree));
    }

    [Theory]
    [InlineData("ready ? 'go' : 'stop'", true, "go")]
    [InlineData("ready ? 'go' : 'stop'", false, "stop")]
    [InlineData("ready ? (verified ? 'verified' : 'pending') : 'blocked'", true, "pending")]
    public void Compile_TernaryOperator_SelectsExpectedBranch(string source, bool ready, string expected) {
        var tree       = _compiler.Parse(source);
        var expression = _compiler.Compile<Student, string>(tree);
        var func       = expression.Compile();

        Assert.Equal(expected, func(new() { Ready = ready }));
    }

    [Theory]
    [InlineData("full_name.contains('lic')", true)]
    [InlineData("full_name.startsWith('Al')", true)]
    [InlineData("full_name.endsWith('ce')", true)]
    [InlineData("full_name.matches('^A.*e$')", true)]
    [InlineData("full_name.size() == 5", true)]
    public void Compile_StringMemberFunctions_ReturnExpectedResult(string source, bool expected) {
        var tree       = _compiler.Parse(source);
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.Equal(expected, func(new() { FullName = "Alice" }));
    }

    [Theory]
    [InlineData("size(full_name) == 5", true)]
    [InlineData("size(scores) == 3", true)]
    public void Compile_SizeFunction_ReturnsStringLengthOrListCount(string source, bool expected) {
        var tree       = _compiler.Parse(source);
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.Equal(expected, func(new() { FullName = "Alice", Scores = [70L, 85L, 95L] }));
    }

    [Theory]
    [InlineData("2 in [1, 2, 3]", true)]
    [InlineData("4 in [1, 2, 3]", false)]
    public void Compile_InOperator_ChecksListMembership(string source, bool expected) {
        var tree       = _compiler.Parse(source);
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.Equal(expected, func(new()));
    }

    [Fact]
    public void Compile_ListLiteral_AllowsMemberSizeAccess() {
        var tree       = _compiler.Parse("[1, 2, 3].size() == 3");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new()));
    }

    [Theory]
    [InlineData("scores.exists(score, score > 90)", true)]
    [InlineData("scores.exists(score, score > 100)", false)]
    [InlineData("scores.all(score, score >= 70)", true)]
    [InlineData("scores.all(score, score > 80)", false)]
    [InlineData("scores.filter(score, score > 80).size() == 2", true)]
    [InlineData("scores.filter(score, score > 90).size() == 1", true)]
    [InlineData("scores.map(score, score + 1).contains(86)", true)]
    [InlineData("scores.map(score, score + 1).contains(70)", false)]
    public void Compile_ListMacros_ReturnExpectedResult(string source, bool expected) {
        var tree       = _compiler.Parse(source);
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.Equal(expected, func(new() { Scores = [70L, 85L, 95L] }));
    }

    [Theory]
    [InlineData("false && full_name.size() > 0", false)]
    [InlineData("true || full_name.size() > 0", true)]
    [InlineData("null && false", false)]
    [InlineData("null || true", true)]
    public void Compile_LogicalOperators_ShortCircuitAndTolerateNull(string source, bool expected) {
        var tree       = _compiler.Parse(source);
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.Equal(expected, func(new() { FullName = null }));
    }

    [Theory]
    [InlineData("full_name && true")]
    [InlineData("1 || false")]
    public void Compile_LogicalOperators_RejectNonBooleanOperands(string source) {
        var tree = _compiler.Parse(source);

        Assert.Throws<ParseException>(() => _compiler.Compile<Student, bool>(tree));
    }

    [Theory]
    [InlineData("'name' in {'name': 'Alice'}", true)]
    [InlineData("'missing' in {'name': 'Alice'}", false)]
    public void Compile_InOperator_ChecksMapKeys(string source, bool expected) {
        var tree       = _compiler.Parse(source);
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.Equal(expected, func(new()));
    }

    #region Nested type: Student

    private sealed class Student
    {
        public string? FullName { get; set; }

        public bool Ready { get; set; }

        public bool Verified { get; set; }

        public long[] Scores { get; set; } = [];
    }

    #endregion

    #region Nested type: StudentProfile

    private sealed class StudentProfile
    {
        public string? FullName { get; set; }
    }

    #endregion
}
