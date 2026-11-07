using System.Collections.Generic;
using Schemata.Expressions.Cel.Expressions;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Cel.Tests.Compilation;

public class CelPushdownPlannerShould
{
    private readonly CelCompiler        _compiler = new();
    private readonly CelPushdownPlanner _planner  = new();

    [Fact]
    public void Push_Whole_When_All_Flat_Comparisons_And_Logical_Operators() {
        var plan = Plan("age >= 18 && status == 'active' || score < 100");

        Assert.True(plan.HasPushed);
        Assert.False(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Regex_Match() {
        var plan = Plan("name.matches('^a')");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Macro_Present() {
        var plan = Plan("tags.exists(x, x == 1)");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Navigation_Chain() {
        var plan = Plan("customer.name == 'x'");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Theory]
    [InlineData("discount / 2 > 10")]
    [InlineData("discount % 2 == 0")]
    public void Push_DivisionAndModulo_WhenArithmeticEnabled(string source) {
        var plan = Plan(source);

        Assert.True(plan.HasPushed);
        Assert.False(plan.HasResidual);
    }

    [Theory]
    [InlineData("discount + 2 > 10")]
    [InlineData("discount - 2 > 10")]
    [InlineData("discount * 2 > 10")]
    public void Keep_Residual_WhenArithmeticRequiresCheckedOverflow(string source) {
        var plan = Plan(source);

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Theory]
    [InlineData("[1, 2] == [1, 2]")]
    [InlineData("{'a': 1} != {'a': 2}")]
    public void Keep_Residual_WhenEqualityRequiresStructuralComparison(string source) {
        var plan = Plan(source);

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Arithmetic_Disabled() {
        var plan = _planner.Plan(_compiler.Parse("discount / 2 > 10"),
                                 new() { Arithmetic = false }, typeof(object));

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Push_Presence_When_Has_Flat_Field() {
        var plan = Plan("has(email)");

        Assert.True(plan.HasPushed);
        Assert.False(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Conjunction_And_Logical_Disabled() {
        var plan = _planner.Plan(_compiler.Parse("status == 'a' && active == true"),
                                 new() { Logical = false }, typeof(object));

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Split_Pushes_Flat_Conjunct_And_Residuals_Navigation() {
        var plan = Plan("age == 1 && customer.name == 'x'");

        Assert.True(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Residuals_Whole_When_Single_Navigation() {
        var plan = Plan("customer.name == 'x'");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Residuals_Whole_When_Disjunction_Mixes_Pushable_And_Residual() {
        var plan = Plan("age == 1 || customer.name == 'x'");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Split_Gives_Distinct_Compile_Cache_Sources() {
        var plan = Plan("age == 1 && customer.name == 'x'");

        var pushed   = Assert.IsAssignableFrom<CelNode>(plan.Pushed);
        var residual = Assert.IsAssignableFrom<CelNode>(plan.Residual);

        Assert.NotEqual(pushed.Source, residual.Source);
        Assert.NotEqual(string.Empty, pushed.Source);
    }

    [Theory]
    [InlineData("tags == other_tags")]
    [InlineData("tags == ['a']")]
    public void Structural_Equality_Remains_Local_While_Scalar_Conjunct_Pushes(string equality) {
        var plan = _planner.Plan(_compiler.Parse("age > 18 && " + equality), ExpressionCapabilities.Relational, typeof(Row));
        Assert.NotNull(plan.Pushed);
        Assert.NotNull(plan.Residual);
        var pushed = _compiler.Compile<Row, bool>(plan.Pushed).Compile();
        var residual = _compiler.Compile<Row, bool>(plan.Residual).Compile();
        var rows = new[] {
            new Row { Age = 20, Tags = ["a"], OtherTags = ["a"] },
            new Row { Age = 20, Tags = ["b"], OtherTags = ["a"] },
            new Row { Age = 10, Tags = ["a"], OtherTags = ["a"] },
        };
        Assert.Single(rows, row => pushed(row) && residual(row));
        Assert.True(pushed(rows[0]) && residual(rows[0]));
    }

    private sealed class Row {
        public int Age { get; init; }
        public List<string> Tags { get; init; } = [];
        public List<string> OtherTags { get; init; } = [];
    }

    private ExpressionPushdownPlan Plan(string source) {
        return _planner.Plan(_compiler.Parse(source), ExpressionCapabilities.Relational, typeof(object));
    }
}
