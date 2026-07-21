using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class PlanExecutorShould
{
    private const string DriverName = "probe";

    [Fact]
    public async Task ExecuteAsync_WhenLimitSpecifiesSkipAndPageSize_ReturnsRangedRowsTotalAndPageToken() {
        var driver = CreateDriver(DriverCapabilities.None, ValueRows(250));
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan     = new LimitNode(Source(), 5, 250);
        var request  = new QueryInsightRequest { PageSize = 250, Skip = 99 };

        var response = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        Assert.Equal(100, response.Rows.Count);
        Assert.Equal(5, response.Rows[0]["value"]);
        Assert.Equal(104, response.Rows[^1]["value"]);
        Assert.Equal(250, response.TotalSize);
        Assert.NotNull(response.NextPageToken);
    }

    [Fact]
    public async Task Materializes_All_Rows_Beyond_Page_Cap() {
        var driver = CreateDriver(DriverCapabilities.None, ValueRows(250));
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan     = new LimitNode(Source(), 20, 1);
        var request  = new QueryInsightRequest { PageSize = 1, Skip = 200, PageToken = "not-a-page-token" };

        await using var materialized = await executor.MaterializeAsync(plan, request, null);

        Assert.Equal(250, await CountAsync(materialized.Rows));
    }

    [Fact]
    public async Task Keeps_Nested_Limit_Local_After_Stripping_Top_Level_Pagination() {
        SubPlan? received = null;
        var driver = CreateDriver(DriverCapabilities.None, ValueRows(5), onExecute: plan => received = plan);
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan     = new LimitNode(new LimitNode(Source(), 1, 3), 10, 20);

        await using var materialized = await executor.MaterializeAsync(plan, new(), null);
        var rows = await ReadAsync(materialized.Rows);

        Assert.IsType<SourceNode>(received!.Root);
        Assert.Equal(
            [1, 2, 3],
            rows.Select(row => Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row["p"])["value"])
        );
    }

    [Fact]
    public async Task Streams_Without_Full_Buffering() {
        var sourceCompleted = false;
        var driver = CreateDriver(DriverCapabilities.None, ValueRows(2), onCompleted: () => sourceCompleted = true);
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));

        await using var materialized = await executor.MaterializeAsync(Source(), new(), null);
        await using var rows = materialized.Rows.GetAsyncEnumerator();

        Assert.True(await rows.MoveNextAsync());
        Assert.False(sourceCompleted);
        Assert.True(await rows.MoveNextAsync());
        Assert.False(await rows.MoveNextAsync());
        Assert.True(sourceCompleted);
    }

    [Fact]
    public async Task Pushes_Compute_But_Runs_Group_Locally_When_Only_Compute_Is_Advertised() {
        SubPlan? received = null;
        var rows = new IReadOnlyDictionary<string, object?>[] {
            new Dictionary<string, object?> { ["computed"] = "ready" },
            new Dictionary<string, object?> { ["computed"] = "ready" },
        };
        var driver = CreateDriver(
            DriverCapabilities.Compute,
            rows,
            [new("computed", FieldType.String, "p", false, [])],
            plan => received = plan
        );
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new GroupNode(
            new ComputeNode(Source(), [new("computed", ValueExpression())]),
            ["p.computed"],
            [new("count", AggregationFunction.Count, "p.computed")]
        );

        await using var materialized = await executor.MaterializeAsync(plan, new(), null);
        var grouped = Assert.Single(await ReadAsync(materialized.Rows));

        Assert.IsType<ComputeNode>(received!.Root);
        Assert.Equal("ready", grouped["computed"]);
        Assert.Equal(2, grouped["count"]);
    }

    [Fact]
    public async Task Aggregates_Min_Max_Over_Mixed_Numeric_Types_Locally() {
        var rows = new IReadOnlyDictionary<string, object?>[] {
            new Dictionary<string, object?> { ["bucket"] = "a", ["value"] = 3 },
            new Dictionary<string, object?> { ["bucket"] = "a", ["value"] = 5L },
            new Dictionary<string, object?> { ["bucket"] = "a", ["value"] = 4.5 },
        };
        var driver = CreateDriver(
            DriverCapabilities.None,
            rows,
            [new("value", FieldType.Int64, "p", false, [])],
            _ => { }
        );
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new GroupNode(
            Source(),
            ["p.bucket"],
            [
                new("smallest", AggregationFunction.Min, "p.value"),
                new("largest", AggregationFunction.Max, "p.value"),
            ]
        );

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        var row = Assert.Single(response.Rows);

        Assert.Equal(3, row["smallest"]);
        Assert.Equal(5L, row["largest"]);
    }

    [Fact]
    public async Task Materializes_Nested_Selection_In_Driver_Then_Runs_Child_Pipeline_Locally() {
        SubPlan? received = null;
        var driver = CreateDriver(
            DriverCapabilities.Nested,
            NestedRows(),
            NestedSchema(),
            plan => received = plan
        );
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [NestedItem()]);

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        var row = Assert.Single(response.Rows);
        var children = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["children"]);
        var child = Assert.Single(children);

        var pushed = Assert.IsType<SelectionNode>(received!.Root);
        Assert.Collection(pushed.Items, item => Assert.Equal(SelectionKind.Nested, item.Kind));
        Assert.Equal("first", child["name"]);
        AssertSchemaMatchesRows(response, ["children"]);
    }

    [Fact]
    public async Task Evaluates_Expression_Selection_Locally_And_Returns_Its_Schema() {
        SubPlan? received = null;
        var compiler = CreateValueCompiler();
        var driver = CreateDriver(
            DriverCapabilities.Project,
            ValueRows(1),
            [new("value", FieldType.Int64, "p", false, [])],
            plan => received = plan
        );
        await using var services = CreateServices(driver, compiler.Object);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [ExpressionItem()]);

        await using var materialized = await executor.MaterializeAsync(plan, new(), null);
        var rows = await ReadAsync(materialized.Rows);
        var row = Assert.Single(rows);

        Assert.IsType<SourceNode>(received!.Root);
        Assert.Equal("computed-0", row["computed"]);
        AssertSchemaMatchesRows(materialized.Schema, rows, ["computed"]);
    }

    [Fact]
    public async Task Materializes_Mixed_Selection_On_Both_Sides_And_Aligns_Schema_With_Local_Output() {
        SubPlan? received = null;
        var compiler = CreateValueCompiler();
        var driver = CreateDriver(
            DriverCapabilities.Nested,
            NestedRows(),
            NestedSchema(),
            plan => received = plan
        );
        await using var services = CreateServices(driver, compiler.Object);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [ExpressionItem(), NestedItem()]);

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        var row = Assert.Single(response.Rows);
        var children = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["children"]);

        var pushed = Assert.IsType<SelectionNode>(received!.Root);
        Assert.Collection(pushed.Items, item => Assert.Equal(SelectionKind.Nested, item.Kind));
        Assert.Equal("computed-7", row["computed"]);
        Assert.Equal("first", Assert.Single(children)["name"]);
        AssertSchemaMatchesRows(response, ["children", "computed"]);
    }

    [Fact]
    public async Task Aligns_Multi_Source_Schema_With_Local_Selection_Output() {
        var compiler = CreateValueCompiler();
        Expression<Func<IReadOnlyDictionary<string, object?>, bool>> match = _ => true;
        compiler.Setup(value => value.Compile<IReadOnlyDictionary<string, object?>, bool>(
                           It.IsAny<IExpressionTree>(),
                           It.IsAny<ExpressionCompileOptions?>()))
                .Returns(match);

        var driver = new Mock<ISourceDriver>(MockBehavior.Strict);
        driver.SetupGet(value => value.Capabilities).Returns(DriverCapabilities.None);
        driver.Setup(value => value.ExecuteAsync(
                         It.IsAny<SubPlan>(),
                         It.IsAny<QueryInsightRequest>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()))
              .Returns((SubPlan subPlan, QueryInsightRequest _, ClaimsPrincipal? _, CancellationToken ct)
                           => ValueTask.FromResult<ISourceResult>(CreateResult(
                                  subPlan.SourceAlias == "p"
                                      ? NestedRows()
                                      : [new Dictionary<string, object?> { ["id"] = 1 }],
                                  ValueSchema(),
                                  null,
                                  null,
                                  ct).Object));

        await using var services = CreateServices(driver, compiler.Object);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var left  = Source() with { SourceSet = ["p"] };
        var right = new SourceNode("q", new(DriverName, new Dictionary<string, object?>())) { SourceSet = ["q"] };
        var join = new JoinNode(left, right, JoinKind.Inner, ValueExpression()) { SourceSet = ["p", "q"] };
        var plan = new SelectionNode(join, [
            new("value", SelectionKind.Field, "p.value", null, [], null),
            ExpressionItem(),
            NestedItem(),
        ]) { SourceSet = ["p", "q"] };

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        AssertSchemaMatchesRows(response, ["value", "children", "computed"]);

        await using var materialized = await executor.MaterializeAsync(plan, new(), null);
        Assert.Equal(["value", "children", "computed"], materialized.Schema.Select(field => field.Name));
    }

    [Fact]
    public async Task Runs_Child_Pipeline_On_Dictionary_Children_When_Driver_Lacks_Nested() {
        SubPlan? received = null;
        var compiler = CreateValueCompiler();
        Expression<Func<IReadOnlyDictionary<string, object?>, bool>> predicate = row => NotSecond(row);
        compiler.Setup(value => value.Compile<IReadOnlyDictionary<string, object?>, bool>(
                           It.IsAny<IExpressionTree>(),
                           It.IsAny<ExpressionCompileOptions?>()))
                .Returns(predicate);
        var driver = CreateDriver(
            DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order,
            NestedRows(),
            NestedSchema(),
            plan => received = plan
        );
        await using var services = CreateServices(driver, compiler.Object);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [NestedItem("children", "p.children", 2, ValueExpression())]);

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        var row = Assert.Single(response.Rows);
        var children = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["children"]);

        Assert.IsType<SourceNode>(received!.Root);
        Assert.Equal("first", Assert.Single(children)["name"]);
    }

    [Fact]
    public async Task Materializes_Clr_And_Iterator_Children_In_Local_Fallback() {
        var driver = CreateDriver(
            DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order,
            [new Dictionary<string, object?> { ["value"] = 7, ["children"] = MixedChildren() }],
            NestedSchema()
        );
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [NestedItem("children", "p.children", 10)]);

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        var row = Assert.Single(response.Rows);
        var children = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["children"]);

        Assert.Equal(["dictionary", "entity"], children.Select(child => child["name"]));
    }

    [Fact]
    public async Task Returns_Empty_Children_For_Empty_Or_Null_Child_Values() {
        var driver = CreateDriver(
            DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order,
            [
                new Dictionary<string, object?> { ["value"] = 1, ["children"] = Array.Empty<IReadOnlyDictionary<string, object?>>() },
                new Dictionary<string, object?> { ["value"] = 2, ["children"] = null },
            ],
            NestedSchema()
        );
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [NestedItem()]);

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);

        Assert.Equal(2, response.Rows.Count);
        Assert.All(
            response.Rows,
            row => Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["children"])));
    }

    [Fact]
    public async Task Throws_Unimplemented_With_Driver_Name_When_Child_Field_Is_Missing() {
        var driver = CreateDriver(
            DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order,
            [new Dictionary<string, object?> { ["value"] = 7 }],
            NestedSchema()
        );
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [NestedItem()]);

        var exception = await Assert.ThrowsAsync<InsightValidationException>(
            async () => await executor.ExecuteAsync(plan, new(), null, CancellationToken.None));

        Assert.Equal(InsightReasons.Unimplemented, exception.Reason);
        Assert.Contains(DriverName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("p.children", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Throws_Invalid_Argument_When_Child_Value_Is_Not_A_Collection() {
        foreach (var value in new object[] { "oops", 42 }) {
            var driver = CreateDriver(
                DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order,
                [new Dictionary<string, object?> { ["value"] = 7, ["children"] = value }],
                NestedSchema()
            );
            await using var services = CreateServices(driver);
            var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
            var plan = new SelectionNode(Source(), [NestedItem()]);

            var exception = await Assert.ThrowsAsync<InsightValidationException>(
                async () => await executor.ExecuteAsync(plan, new(), null, CancellationToken.None));

            Assert.Equal(InsightReasons.InvalidArgument, exception.Reason);
        }
    }

    [Fact]
    public async Task Combines_Local_Expression_With_Nested_Fallback() {
        SubPlan? received = null;
        var compiler = CreateValueCompiler();
        var driver = CreateDriver(
            DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order,
            NestedRows(),
            NestedSchema(),
            plan => received = plan
        );
        await using var services = CreateServices(driver, compiler.Object);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [ExpressionItem(), NestedItem()]);

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        var row = Assert.Single(response.Rows);
        var children = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["children"]);

        Assert.IsType<SourceNode>(received!.Root);
        Assert.Equal("computed-7", row["computed"]);
        Assert.Equal("first", Assert.Single(children)["name"]);
    }

    [Fact]
    public async Task Anchors_Nested_Fallback_To_Field_Path_Source_In_Multi_Source_Rows() {
        var compiler = CreateValueCompiler();
        Expression<Func<IReadOnlyDictionary<string, object?>, bool>> match = _ => true;
        compiler.Setup(value => value.Compile<IReadOnlyDictionary<string, object?>, bool>(
                           It.IsAny<IExpressionTree>(),
                           It.IsAny<ExpressionCompileOptions?>()))
                .Returns(match);

        var driver = new Mock<ISourceDriver>(MockBehavior.Strict);
        driver.SetupGet(value => value.Capabilities)
              .Returns(DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order);
        driver.Setup(value => value.ExecuteAsync(
                         It.IsAny<SubPlan>(),
                         It.IsAny<QueryInsightRequest>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()))
              .Returns((SubPlan subPlan, QueryInsightRequest _, ClaimsPrincipal? _, CancellationToken ct)
                           => ValueTask.FromResult<ISourceResult>(CreateResult(
                                  subPlan.SourceAlias == "p"
                                      ? [new Dictionary<string, object?> {
                                            ["value"]    = 7,
                                            ["children"] = new IReadOnlyDictionary<string, object?>[] {
                                                new Dictionary<string, object?> { ["name"] = "p-child" },
                                            },
                                        }]
                                      : [new Dictionary<string, object?> {
                                            ["id"]       = 1,
                                            ["children"] = new IReadOnlyDictionary<string, object?>[] {
                                                new Dictionary<string, object?> { ["name"] = "q-child" },
                                            },
                                        }],
                                  ValueSchema(),
                                  null,
                                  null,
                                  ct).Object));

        await using var services = CreateServices(driver, compiler.Object);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var left  = Source() with { SourceSet = ["p"] };
        var right = new SourceNode("q", new(DriverName, new Dictionary<string, object?>())) { SourceSet = ["q"] };
        var join = new JoinNode(left, right, JoinKind.Inner, ValueExpression()) { SourceSet = ["p", "q"] };
        var plan = new SelectionNode(join, [NestedItem()]) { SourceSet = ["p", "q"] };

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        var row = Assert.Single(response.Rows);
        var children = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["children"]);

        Assert.Equal("p-child", Assert.Single(children)["name"]);
    }

    [Fact]
    public async Task Falls_Back_To_Unprefixed_Field_Path_With_Custom_Alias() {
        var driver = CreateDriver(
            DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order,
            NestedRows(),
            NestedSchema()
        );
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan = new SelectionNode(Source(), [NestedItem("kids", "children", 2)]);

        var response = await executor.ExecuteAsync(plan, new(), null, CancellationToken.None);
        var row = Assert.Single(response.Rows);
        var children = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(row["kids"]);

        Assert.Equal(["first", "second"], children.Select(child => child["name"]));
    }

    [Fact]
    public async Task Skips_Security_Gate_When_Disabled() {
        var access = new Mock<IAccessProvider<SchemataInsightSource, QueryInsightRequest>>(MockBehavior.Strict);
        await using var services = CreateRepositoryDriverServices(access);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));

        await using (var materialized = await executor.MaterializeAsync(RepositorySource(), new(), null, enforceSecurity: false)) {
            await CountAsync(materialized.Rows);
        }

        var exception = await Assert.ThrowsAsync<TargetInvocationException>(async () => await executor.MaterializeAsync(RepositorySource(), new(), null));
        Assert.IsType<MockException>(exception.InnerException);
    }

    [Fact]
    public async Task Surfaces_Permission_Denied_When_Security_Enforced() {
        var access = new Mock<IAccessProvider<SchemataInsightSource, QueryInsightRequest>>();
        access.Setup(provider => provider.HasAccessAsync(
                                     It.IsAny<SchemataInsightSource?>(),
                                     It.IsAny<AccessContext<QueryInsightRequest>>(),
                                     It.IsAny<ClaimsPrincipal?>(),
                                     It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);
        await using var services = CreateRepositoryDriverServices(access);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));

        await Assert.ThrowsAsync<PermissionDeniedException>(async () => await executor.MaterializeAsync(RepositorySource(), new(), null));
    }

    [Fact]
    public async Task Disposes_Source_Result_After_Stream_Cancellation() {
        using var cts = new CancellationTokenSource();
        var disposed = false;
        var driver = CreateDriver(DriverCapabilities.None, ValueRows(2), onDispose: () => disposed = true);
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var materialized = await executor.MaterializeAsync(Source(), new(), null, ct: cts.Token);

        try {
            await using var rows = materialized.Rows.GetAsyncEnumerator();
            Assert.True(await rows.MoveNextAsync());
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await rows.MoveNextAsync().AsTask());
        } finally {
            await materialized.DisposeAsync();
        }

        Assert.True(disposed);
    }

    [Fact]
    public async Task Creates_And_Disposes_Scoped_Repository_Dependency_Per_Execution() {
        var tracker = new ScopeTracker();
        await using var services = CreateScopeValidatingRepositoryDriverServices(tracker);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));

        var response = await executor.ExecuteAsync(RepositorySource(), new(), null, CancellationToken.None);

        Assert.Single(response.Rows);
        Assert.Equal(1, tracker.Created);
        Assert.Equal(1, tracker.Disposed);

        await using (var materialized = await executor.MaterializeAsync(RepositorySource(), new(), null, enforceSecurity: false)) {
            Assert.Equal(1, await CountAsync(materialized.Rows));
            Assert.Equal(2, tracker.Created);
            Assert.Equal(1, tracker.Disposed);
        }

        Assert.Equal(2, tracker.Disposed);
    }

    [Fact]
    public async Task Disposes_Scoped_Repository_Dependency_After_Cancelled_Materialized_Stream() {
        var tracker = new ScopeTracker();
        await using var services = CreateScopeValidatingRepositoryDriverServices(tracker, 2);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        using var cts = new CancellationTokenSource();
        var materialized = await executor.MaterializeAsync(RepositorySource(), new(), null, enforceSecurity: false, ct: cts.Token);

        try {
            await using var rows = materialized.Rows.GetAsyncEnumerator();
            Assert.True(await rows.MoveNextAsync());
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await rows.MoveNextAsync().AsTask());
        } finally {
            await materialized.DisposeAsync();
        }

        Assert.Equal(1, tracker.Created);
        Assert.Equal(1, tracker.Disposed);
    }

    private static ServiceProvider CreateServices(Mock<ISourceDriver> driver, IExpressionCompiler? compiler = null) {
        var services = new ServiceCollection()
                      .AddKeyedSingleton<ISourceDriver>(DriverName, driver.Object);
        if (compiler is not null) {
            services.AddKeyedSingleton<IExpressionCompiler>("test", compiler);
        }

        return services.BuildServiceProvider();
    }

    private static SourceNode Source() {
        return new("p", new(DriverName, new Dictionary<string, object?>()));
    }

    private static SourceNode RepositorySource() {
        return new("r", new(RepositoryDriver.DriverName, new Dictionary<string, object?> { ["resource"] = "insightSources" }));
    }

    private static ServiceProvider CreateRepositoryDriverServices(
        Mock<IAccessProvider<SchemataInsightSource, QueryInsightRequest>> access
    ) {
        var repository = new Mock<IRepository<SchemataInsightSource>>();
        repository.Setup(r => r.ListAsync<SchemataInsightSource>(
                              It.IsAny<Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>> query,
                            CancellationToken                                                         ct)
                           => EntityRows(query(new[] { new SchemataInsightSource { Name = "source" } }.AsQueryable()), ct));

        return new ServiceCollection()
              .AddSingleton(repository.Object)
              .AddSingleton(access.Object)
              .AddKeyedSingleton<ISourceDriver>(RepositoryDriver.DriverName, static (services, _) => new RepositoryDriver(services))
               .BuildServiceProvider();
    }

    private static ServiceProvider CreateScopeValidatingRepositoryDriverServices(ScopeTracker tracker, int rowCount = 1) {
        var repository = new Mock<IRepository<SchemataInsightSource>>();
        repository.Setup(r => r.ListAsync<SchemataInsightSource>(
                              It.IsAny<Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>> query,
                            CancellationToken                                                         ct)
                           => EntityRows(
                               query(Enumerable.Range(0, rowCount)
                                               .Select(value => new SchemataInsightSource { Name = $"source-{value}" })
                                               .AsQueryable()),
                               ct));

        return new ServiceCollection()
              .AddScoped<IDisposable>(_ => {
                  tracker.Created++;
                  var probe = new Mock<IDisposable>();
                  probe.Setup(disposable => disposable.Dispose()).Callback(() => tracker.Disposed++);
                  return probe.Object;
              })
              .AddTransient<IRepository<SchemataInsightSource>>(services => {
                  _ = services.GetRequiredService<IDisposable>();
                  return repository.Object;
              })
              .AddKeyedSingleton<ISourceDriver>(RepositoryDriver.DriverName, static (services, _) => new RepositoryDriver(services))
              .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async IAsyncEnumerable<SchemataInsightSource> EntityRows(
        IEnumerable<SchemataInsightSource> rows,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var row in rows) {
            ct.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }

    private static async Task<int> CountAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows) {
        var count = 0;
        await foreach (var _ in rows) {
            count++;
        }

        return count;
    }

    private static Mock<ISourceDriver> CreateDriver(
        DriverCapabilities                                capabilities,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<FieldDescriptor>?                    schema = null,
        Action<SubPlan>?                                   onExecute = null,
        Action?                                            onDispose = null,
        Action?                                            onCompleted = null
    ) {
        var driver = new Mock<ISourceDriver>(MockBehavior.Strict);
        driver.SetupGet(value => value.Capabilities).Returns(capabilities);
        driver.Setup(value => value.ExecuteAsync(
                         It.IsAny<SubPlan>(),
                         It.IsAny<QueryInsightRequest>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()))
              .Returns((SubPlan subPlan, QueryInsightRequest _, ClaimsPrincipal? _, CancellationToken ct) => {
                  onExecute?.Invoke(subPlan);
                  return ValueTask.FromResult<ISourceResult>(CreateResult(rows, schema ?? ValueSchema(), onDispose, onCompleted, ct).Object);
              });
        return driver;
    }

    private static Mock<ISourceResult> CreateResult(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<FieldDescriptor>                        schema,
        Action?                                               onDispose,
        Action?                                               onCompleted,
        CancellationToken                                     ct
    ) {
        var result = new Mock<ISourceResult>(MockBehavior.Strict);
        result.SetupGet(value => value.Rows).Returns(Stream(rows, onCompleted, ct));
        result.SetupGet(value => value.Schema).Returns(schema);
        result.Setup(value => value.DisposeAsync()).Returns(() => {
            onDispose?.Invoke();
            return ValueTask.CompletedTask;
        });
        return result;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ValueRows(int count) {
        return Enumerable.Range(0, count)
                         .Select(value => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["value"] = value })
                         .ToArray();
    }

    private static IReadOnlyList<FieldDescriptor> ValueSchema() {
        return [new("value", FieldType.Int64, null, false, [])];
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> NestedRows() {
        return [new Dictionary<string, object?> {
            ["value"] = 7,
            ["children"] = new IReadOnlyDictionary<string, object?>[] {
                new Dictionary<string, object?> { ["name"] = "first" },
                new Dictionary<string, object?> { ["name"] = "second" },
            },
        }];
    }

    private static IReadOnlyList<FieldDescriptor> NestedSchema() {
        return [new("children", FieldType.Object, null, true, [new("name", FieldType.String, null, false, [])])];
    }

    private static SelectionItem NestedItem() {
        return NestedItem("children", "p.children", 1);
    }

    private static SelectionItem NestedItem(string alias, string? fieldPath, int pageSize, ParsedExpression? filter = null) {
        var children = ImmutableArray.Create(new SelectionItem("name", SelectionKind.Field, "children.name", null, [], null));
        PlanNode pipeline = new SourceNode("children", new(DriverName, new Dictionary<string, object?>()));
        if (filter is not null) {
            pipeline = new FilterNode(pipeline, filter);
        }

        pipeline = new LimitNode(pipeline, 0, pageSize);
        pipeline = new SelectionNode(pipeline, children);
        return new(alias, SelectionKind.Nested, fieldPath, null, children, pipeline);
    }

    private static SelectionItem ExpressionItem() {
        return new("computed", SelectionKind.Expression, null, ValueExpression(), [], null);
    }

    private static ParsedExpression ValueExpression() {
        return new(new Mock<IExpressionTree>().Object, "test", ExpressionKind.Value);
    }

    private static Mock<IExpressionCompiler> CreateValueCompiler() {
        Expression<Func<IReadOnlyDictionary<string, object?>, object>> expression = row => ComputedValue(row);
        var compiler = new Mock<IExpressionCompiler>(MockBehavior.Strict);
        compiler.Setup(value => value.Compile<IReadOnlyDictionary<string, object?>, object>(
                           It.IsAny<IExpressionTree>(),
                           It.IsAny<ExpressionCompileOptions?>()))
                .Returns(expression);
        return compiler;
    }

    private static object ComputedValue(IReadOnlyDictionary<string, object?> row) {
        var source = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row["p"]);
        return $"computed-{source["value"]}";
    }

    private static bool NotSecond(IReadOnlyDictionary<string, object?> row) {
        var child = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row["children"]);
        return !Equals(child["name"], "second");
    }

    private static IEnumerable<object> MixedChildren() {
        yield return new Dictionary<string, object?> { ["name"] = "dictionary" };
        yield return new ChildEntity { Name = "entity" };
    }

    private sealed class ChildEntity
    {
        public string? Name { get; init; }
    }

    private static void AssertSchemaMatchesRows(QueryInsightResponse response, IReadOnlyList<string> names) {
        AssertSchemaMatchesRows(response.Schema, response.Rows, names);
    }

    private static void AssertSchemaMatchesRows(
        IReadOnlyList<FieldDescriptor>                 schema,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<string>                         names
    ) {
        var row = Assert.Single(rows);
        Assert.Equal(names, schema.Select(field => field.Name));
        Assert.Equal(names, row.Keys);
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows
    ) {
        var materialized = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in rows) {
            materialized.Add(row);
        }

        return materialized;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Stream(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        Action?                                            completed,
        [EnumeratorCancellation] CancellationToken         ct
    ) {
        foreach (var row in rows) {
            ct.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }

        completed?.Invoke();
    }

    private sealed class ScopeTracker
    {
        public int Created { get; set; }

        public int Disposed { get; set; }
    }
}
