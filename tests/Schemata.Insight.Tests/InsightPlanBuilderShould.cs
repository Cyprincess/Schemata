using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class InsightPlanBuilderShould
{
    [Fact]
    public async Task Builds_Source_Filter_Order_Selection_Limit_Chain() {
        var builder = Builder(("customers", "customers"));
        var request = new QueryInsightRequest {
            Sources = { new("c", "customers") },
            Transformations = {
                new() { Filter  = new(new InsightExpression("age = 1")) },
                new() { OrderBy = new("age desc") },
            },
            Selections = { new() { Field = "c.name" } },
            PageSize   = 10,
        };

        var plan = await builder.BuildAsync(request, CancellationToken.None);

        var limit = Assert.IsType<LimitNode>(plan);
        Assert.Equal(10, limit.Take);

        var selection = Assert.IsType<SelectionNode>(limit.Input);
        Assert.Equal("name", Assert.Single(selection.Items).Alias);

        var order  = Assert.IsType<OrderNode>(selection.Input);
        var filter = Assert.IsType<FilterNode>(order.Input);
        Assert.Equal("aip", filter.Predicate.Language);
        Assert.IsType<SourceNode>(filter.Input);
    }

    [Fact]
    public async Task Rejects_Unknown_Source() {
        var builder = Builder(("customers", "customers"));
        var request = new QueryInsightRequest { Sources = { new("x", "missing") } };

        var ex = await Assert.ThrowsAsync<InsightValidationException>(
            () => builder.BuildAsync(request, CancellationToken.None).AsTask());

        Assert.Equal(InsightReasons.UnknownSourceName, ex.Reason);
    }

    [Fact]
    public async Task Rejects_Unknown_Language() {
        var builder = Builder(("customers", "customers"));
        var request = new QueryInsightRequest {
            Sources         = { new("c", "customers") },
            Transformations = { new() { Filter = new(new InsightExpression("age = 1", "feel")) } },
        };

        var ex = await Assert.ThrowsAsync<InsightValidationException>(
            () => builder.BuildAsync(request, CancellationToken.None).AsTask());

        Assert.Equal(InsightReasons.UnknownExpressionLanguage, ex.Reason);
    }

    [Fact]
    public async Task Builds_A_GroupBy_Into_A_GroupNode() {
        var builder = Builder(("customers", "customers"));
        var request = new QueryInsightRequest {
            Sources = { new("c", "customers") },
            Transformations = {
                new() {
                    GroupBy = new(["grade"], [new("age", AggregationFunction.Sum, "total_age")]),
                },
            },
        };

        var plan      = await builder.BuildAsync(request, CancellationToken.None);
        var selection = Assert.IsType<SelectionNode>(Assert.IsType<LimitNode>(plan).Input);
        var group     = Assert.IsType<GroupNode>(selection.Input);

        Assert.Equal("grade", Assert.Single(group.Keys));
        var aggregation = Assert.Single(group.Aggregations);
        Assert.Equal("total_age", aggregation.Alias);
        Assert.Equal(AggregationFunction.Sum, aggregation.Function);
    }

    [Fact]
    public async Task Builds_A_Compute_Field_With_A_Value_Capable_Language() {
        var builder = Builder(cel: true, ("customers", "customers"));
        var request = new QueryInsightRequest {
            Sources = { new("c", "customers") },
            Transformations = {
                new() {
                    Compute = new([new(new InsightExpression("age + 1", "cel"), "next_age")]),
                },
            },
        };

        var plan      = await builder.BuildAsync(request, CancellationToken.None);
        var selection = Assert.IsType<SelectionNode>(Assert.IsType<LimitNode>(plan).Input);
        var compute   = Assert.IsType<ComputeNode>(selection.Input);

        var field = Assert.Single(compute.Fields);
        Assert.Equal("next_age", field.Alias);
        Assert.Equal(ExpressionKind.Value, field.Expression.Kind);
        Assert.Equal("cel", field.Expression.Language);
    }

    [Fact]
    public async Task Rejects_A_Compute_Field_On_A_Non_Value_Capable_Language() {
        var builder = Builder(("customers", "customers"));
        var request = new QueryInsightRequest {
            Sources = { new("c", "customers") },
            Transformations = {
                new() {
                    Compute = new([new(new InsightExpression("age + 1"), "next_age")]),
                },
            },
        };

        var ex = await Assert.ThrowsAsync<InsightValidationException>(
            () => builder.BuildAsync(request, CancellationToken.None).AsTask());

        Assert.Equal(InsightReasons.ExpressionLanguageNotValueCapable, ex.Reason);
    }

    [Fact]
    public async Task Folds_Two_Sources_And_A_Join_Into_A_JoinNode() {
        var builder = Builder(true, ("customers", "customers"), ("orders", "orders"));
        var request = new QueryInsightRequest {
            Sources = { new("c", "customers"), new("o", "orders") },
            Joins   = { new("c", "o", JoinKind.Inner, new InsightExpression("c.id == o.customer_id", "cel")) },
        };

        var plan      = await builder.BuildAsync(request, CancellationToken.None);
        var selection = Assert.IsType<SelectionNode>(Assert.IsType<LimitNode>(plan).Input);
        var join      = Assert.IsType<JoinNode>(selection.Input);

        Assert.Equal(JoinKind.Inner, join.Kind);
        Assert.Equal("c", Assert.IsType<SourceNode>(join.Left).Alias);
        Assert.Equal("o", Assert.IsType<SourceNode>(join.Right).Alias);
        Assert.Equal(["c", "o"], join.SourceSet.OrderBy(a => a));
    }

    [Fact]
    public async Task Rejects_A_Join_Referencing_An_Unknown_Alias() {
        var builder = Builder(true, ("customers", "customers"), ("orders", "orders"));
        var request = new QueryInsightRequest {
            Sources = { new("c", "customers"), new("o", "orders") },
            Joins   = { new("c", "x", JoinKind.Inner, new InsightExpression("c.id == o.customer_id", "cel")) },
        };

        var ex = await Assert.ThrowsAsync<InsightValidationException>(
            () => builder.BuildAsync(request, CancellationToken.None).AsTask());

        Assert.Equal(InsightReasons.InvalidArgument, ex.Reason);
    }

    [Fact]
    public async Task Rejects_Disconnected_Sources_Without_A_Join() {
        var builder = Builder(true, ("customers", "customers"), ("orders", "orders"));
        var request = new QueryInsightRequest {
            Sources = { new("c", "customers"), new("o", "orders") },
        };

        var ex = await Assert.ThrowsAsync<InsightValidationException>(
            () => builder.BuildAsync(request, CancellationToken.None).AsTask());

        Assert.Equal(InsightReasons.InvalidArgument, ex.Reason);
    }

    private static InsightPlanBuilder Builder(params (string Name, string Resource)[] sources) {
        return Builder(false, sources);
    }

    private static InsightPlanBuilder Builder(bool cel, params (string Name, string Resource)[] sources) {
        var services = new ServiceCollection();
        services.AddAipExpressions();
        services.AddOrderExpressions();
        if (cel) {
            services.AddCelExpressions();
        }

        var catalog = new InMemoryInsightSourceCatalog(
            sources.ToDictionary(
                source => source.Name,
                source => new SourceConfig("repository", new Dictionary<string, object?> { ["resource"] = source.Resource })));
        services.AddSingleton<IInsightSourceCatalog>(catalog);

        return new InsightPlanBuilder([catalog], services.BuildServiceProvider(), Options.Create(new SchemataInsightOptions()));
    }
}
