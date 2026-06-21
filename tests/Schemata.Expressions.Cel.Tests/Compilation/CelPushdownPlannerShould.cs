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

    [Fact]
    public void Push_Arithmetic_When_Arithmetic_Enabled() {
        var plan = Plan("discount * 2 > 10");

        Assert.True(plan.HasPushed);
        Assert.False(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Arithmetic_Disabled() {
        var plan = _planner.Plan(_compiler.Parse("discount * 2 > 10"),
                                 new() { Arithmetic = false });

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
                                 new() { Logical = false });

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

    private ExpressionPushdownPlan Plan(string source) {
        return _planner.Plan(_compiler.Parse(source), ExpressionCapabilities.Relational);
    }
}
