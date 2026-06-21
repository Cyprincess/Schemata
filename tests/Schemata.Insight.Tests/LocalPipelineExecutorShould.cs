using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class LocalPipelineExecutorShould
{
    [Fact]
    public async Task Filters_Groups_Aggregates_Orders_And_Limits() {
        var executor = Executor();
        var rows = Rows(
            Flat(("grade", 1), ("age", 10)),
            Flat(("grade", 1), ("age", 20)),
            Flat(("grade", 2), ("age", 30)),
            Flat(("grade", 2), ("age", 40)),
            Flat(("grade", 3), ("age", 5)));

        var stages = new PlanNode[] {
            Filter("s.age >= 10"),
            new GroupNode(Source(), ["s.grade"], [new("total", AggregationFunction.Sum, "s.age")]),
            Having("total > 25"),
            Order("total desc"),
            new LimitNode(Source(), null, 1),
            Selection(),
        };

        var result = await Collect(executor.RunAsync(rows, "s", stages, CancellationToken.None));

        var row = Assert.Single(result);
        Assert.Equal(2, row["grade"]);
        Assert.Equal(70d, row["total"]);
    }

    [Fact]
    public async Task Computes_A_Field_Then_Groups_By_It() {
        var executor = Executor();
        var rows = Rows(
            Flat(("age", 20)),
            Flat(("age", 20)),
            Flat(("age", 30)));

        var stages = new PlanNode[] {
            new ComputeNode(Source(), [new("decade", Value("s.age / 10"))]),
            new GroupNode(Source(), ["decade"], [new("count", AggregationFunction.Count, "decade")]),
            Order("decade asc"),
            Selection(),
        };

        var result = await Collect(executor.RunAsync(rows, "s", stages, CancellationToken.None));

        Assert.Equal(2, result.Count);
        Assert.Equal(2d, result[0]["decade"]);
        Assert.Equal(2, result[0]["count"]);
        Assert.Equal(3d, result[1]["decade"]);
        Assert.Equal(1, result[1]["count"]);
    }

    [Fact]
    public async Task Inner_Joins_On_The_Predicate() {
        var executor = Executor();
        var left     = AliasRows("c", Flat(("id", 1)), Flat(("id", 2)));
        var right    = AliasRows("o", Flat(("customer_id", 1), ("amt", 10)), Flat(("customer_id", 1), ("amt", 20)));

        var result = await Collect(executor.JoinAsync(left, right, OnCel("c.id == o.customer_id"),
                                                      JoinKind.Inner, CancellationToken.None));

        Assert.Equal(2, result.Count);
        Assert.All(result, row => Assert.Equal(1, Inner(row, "c")["id"]));
    }

    [Fact]
    public async Task Left_Joins_Keep_Unmatched_Outer_Rows() {
        var executor = Executor();
        var left     = AliasRows("c", Flat(("id", 1)), Flat(("id", 2)));
        var right    = AliasRows("o", Flat(("customer_id", 1), ("amt", 10)));

        var result = await Collect(executor.JoinAsync(left, right, OnCel("c.id == o.customer_id"),
                                                      JoinKind.Left, CancellationToken.None));

        Assert.Equal(2, result.Count);
        var unmatched = Assert.Single(result, row => !row.ContainsKey("o"));
        Assert.Equal(2, Inner(unmatched, "c")["id"]);
    }

    [Fact]
    public async Task Full_Joins_Emit_Both_Unmatched_Sides() {
        var executor = Executor();
        var left     = AliasRows("c", Flat(("id", 1)), Flat(("id", 2)));
        var right    = AliasRows("o", Flat(("customer_id", 1)), Flat(("customer_id", 3)));

        var result = await Collect(executor.JoinAsync(left, right, OnCel("c.id == o.customer_id"),
                                                      JoinKind.Full, CancellationToken.None));

        Assert.Equal(3, result.Count);
        Assert.Single(result, row => row.ContainsKey("c") && row.ContainsKey("o"));
        Assert.Single(result, row => row.ContainsKey("c") && !row.ContainsKey("o"));
        Assert.Single(result, row => !row.ContainsKey("c") && row.ContainsKey("o"));
    }

    private static LocalPipelineExecutor Executor() {
        var services = new ServiceCollection();
        services.AddAipExpressions();
        services.AddCelExpressions();
        services.AddOrderExpressions();
        return new LocalPipelineExecutor(services.BuildServiceProvider());
    }

    private static ParsedExpression OnCel(string predicate) {
        return new ParsedExpression(CelCompiler().Parse(predicate), ExpressionLanguages.Cel, ExpressionKind.Predicate);
    }

    private static IReadOnlyDictionary<string, object?> Inner(IReadOnlyDictionary<string, object?> row, string alias) {
        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row[alias]);
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> AliasRows(
        string                                  alias,
        params IReadOnlyDictionary<string, object?>[] inner
    ) {
        foreach (var row in inner) {
            yield return await Task.FromResult(
                (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { [alias] = row });
        }
    }

    private static FilterNode Filter(string predicate) {
        return new FilterNode(Source(), Parse(predicate, ExpressionKind.Predicate));
    }

    private static FilterNode Having(string predicate) {
        return new FilterNode(Source(), new ParsedExpression(CelCompiler().Parse(predicate), ExpressionLanguages.Cel,
                                                            ExpressionKind.Predicate));
    }

    private static OrderNode Order(string orderBy) {
        return new OrderNode(Source(), orderBy);
    }

    private static SelectionNode Selection() {
        return new SelectionNode(Source(), ImmutableArray<SelectionItem>.Empty);
    }

    private static ParsedExpression Value(string source) {
        return Parse(source, ExpressionKind.Value);
    }

    private static ParsedExpression Parse(string source, ExpressionKind kind) {
        var compiler = kind is ExpressionKind.Value ? CelCompiler() : AipCompiler();
        var language = kind is ExpressionKind.Value ? ExpressionLanguages.Cel : ExpressionLanguages.Aip;
        return new ParsedExpression(compiler.Parse(source), language, kind);
    }

    private static IExpressionCompiler AipCompiler() {
        var services = new ServiceCollection();
        services.AddAipExpressions();
        return services.BuildServiceProvider().GetRequiredKeyedService<IExpressionCompiler>(ExpressionLanguages.Aip);
    }

    private static IExpressionCompiler CelCompiler() {
        var services = new ServiceCollection();
        services.AddCelExpressions();
        return services.BuildServiceProvider().GetRequiredKeyedService<IExpressionCompiler>(ExpressionLanguages.Cel);
    }

    private static SourceNode Source() {
        return new SourceNode("s", new SourceConfig("repository", new Dictionary<string, object?>()));
    }

    private static IReadOnlyDictionary<string, object?> Flat(params (string Key, object? Value)[] fields) {
        var row = new Dictionary<string, object?>();
        foreach (var (key, value) in fields) {
            row[key] = value;
        }

        return row;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows(
        params IReadOnlyDictionary<string, object?>[] rows
    ) {
        foreach (var row in rows) {
            yield return await Task.FromResult(row);
        }
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> Collect(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows
    ) {
        var result = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in rows) {
            result.Add(row);
        }

        return result;
    }
}
