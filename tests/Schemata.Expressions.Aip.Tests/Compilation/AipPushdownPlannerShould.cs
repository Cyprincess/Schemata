using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Aip.Tests.Compilation;

public class AipPushdownPlannerShould
{
    private readonly AipCompiler        _compiler = new();
    private readonly AipPushdownPlanner _planner  = new();

    [Fact]
    public void Push_Whole_When_All_Flat_Comparisons() {
        var plan = Plan("age >= 18 AND status = 'active'");

        Assert.True(plan.HasPushed);
        Assert.False(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Function_Present() {
        var plan = Plan("regex(name, 'a')");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Navigation_Chain() {
        var plan = Plan("customer.name = 'x'");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Disjunction_And_Logical_Disabled() {
        var plan = _planner.Plan(_compiler.Parse("status = 'a' OR status = 'b'"),
                                 new() { Logical = false });

        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Push_Disjunction_When_Logical_Enabled() {
        var plan = Plan("status = 'a' OR status = 'b'");

        Assert.True(plan.HasPushed);
        Assert.False(plan.HasResidual);
    }

    [Fact]
    public void Keep_Residual_When_Wildcard_Disabled() {
        var plan = _planner.Plan(_compiler.Parse("name = 'a*'"),
                                 new() { Wildcard = false });

        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Push_Wildcard_When_Enabled() {
        var plan = Plan("name = 'a*'");

        Assert.True(plan.HasPushed);
        Assert.False(plan.HasResidual);
    }

    [Fact]
    public void Push_Bare_Field_As_Presence() {
        var plan = Plan("active");

        Assert.True(plan.HasPushed);
        Assert.False(plan.HasResidual);
    }

    [Fact]
    public void Split_Pushes_Flat_Conjunct_And_Residuals_Navigation() {
        var plan = Plan("age = 1 AND profile.locale = 'en'");

        Assert.True(plan.HasPushed);
        Assert.True(plan.HasResidual);

        var pushed   = _compiler.Compile<Row, bool>(plan.Pushed!).Compile();
        var residual = _compiler.Compile<Row, bool>(plan.Residual!).Compile();

        Assert.True(pushed(new Row { Age  = 1 }));
        Assert.False(pushed(new Row { Age = 2 }));
        Assert.True(residual(new Row { Profile  = new() { Locale = "en" } }));
        Assert.False(residual(new Row { Profile = new() { Locale = "fr" } }));
    }

    [Fact]
    public void Pushed_And_Residual_Together_Equal_Original() {
        var plan     = Plan("age = 1 AND profile.locale = 'en'");
        var pushed   = _compiler.Compile<Row, bool>(plan.Pushed!).Compile();
        var residual = _compiler.Compile<Row, bool>(plan.Residual!).Compile();
        var original = _compiler.Compile<Row, bool>(_compiler.Parse("age = 1 AND profile.locale = 'en'")).Compile();

        Row[] rows = [
            new() { Age = 1, Profile = new() { Locale = "en" } },
            new() { Age = 1, Profile = new() { Locale = "fr" } },
            new() { Age = 2, Profile = new() { Locale = "en" } },
            new() { Age = 1 },
        ];

        foreach (var row in rows) {
            Assert.Equal(original(row), pushed(row) && residual(row));
        }
    }

    [Fact]
    public void Residuals_Whole_When_Single_Navigation() {
        var plan = Plan("profile.locale = 'en'");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    [Fact]
    public void Residuals_Whole_When_Disjunction_Mixes_Pushable_And_Residual() {
        var plan = Plan("age = 1 OR profile.locale = 'en'");

        Assert.False(plan.HasPushed);
        Assert.True(plan.HasResidual);
    }

    private ExpressionPushdownPlan Plan(string source) {
        return _planner.Plan(_compiler.Parse(source), ExpressionCapabilities.Relational);
    }

    #region Nested types

    private sealed class Row
    {
        public int     Age     { get; set; }
        public Nested? Profile { get; set; }
    }

    private sealed class Nested
    {
        public string? Locale { get; set; }
    }

    #endregion
}
