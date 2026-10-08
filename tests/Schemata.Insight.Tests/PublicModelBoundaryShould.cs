using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Core;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Materialization;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton;
using Schemata.Insight.Skeleton.Plan;
using Xunit;

namespace Schemata.Insight.Tests;

public class PublicModelBoundaryShould
{
    [Theory]
    [InlineData("binding", "UNKNOWN_SOURCE_NAME")]
    [InlineData("resource", "INVALID_ARGUMENT")]
    public async Task DirectPlan_RejectsUnregisteredOrObsoleteRepositoryBinding(string parameter, string reason) {
        using var services = new ServiceCollection().BuildServiceProvider();
        var source = new SourceNode("n", new("repository", new Dictionary<string, object?> { [parameter] = "unregistered" })) {
            SourceSet = ImmutableHashSet.Create("n"),
        };
        var executor = new PlanExecutor(services, new(services), Options.Create(new SchemataInsightOptions()));
        var error = await Assert.ThrowsAsync<InsightValidationException>(async () => await executor.MaterializeAsync(source, new(), null));
        Assert.Equal(reason, error.Reason);
    }

    [Fact]
    public void CyclicPublicGraph_RejectsWithoutUnboundedRecursion() {
        var node = new Node { Value = "visible" };
        node.Next = node;
        Assert.Throws<InsightValidationException>(() => RowMaterializer.ToRow(node, [], "n"));
    }

    [Fact]
    public void SharedAcyclicPublicGraph_PreservesBothReferencesAndHidesSubtypeMembers() {
        var leaf = new Derived { Value = "visible", Secret = "hidden" };
        var row = RowMaterializer.ToRow(new Pair { Left = leaf, Right = leaf }, [], "p");
        foreach (var key in new[] { "left", "right" }) {
            var value = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row[key]);
            Assert.Equal("visible", value["value"]);
            Assert.False(value.ContainsKey("secret"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectPlan_RejectsHiddenField_EvenWithoutRowSecurity(bool enforceSecurity) {
        var services = new ServiceCollection();
        new SchemataInsightBuilder(new SchemataOptions(), services)
            .AddRepositorySource<Node, Node>("nodes", n => n)
            .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
        using var provider = services.BuildServiceProvider();
        var source = new SourceNode("n", new("repository", new Dictionary<string, object?> { ["binding"] = "nodes" })) {
            SourceSet = ImmutableHashSet.Create("n"),
        };
        var plan = new SelectionNode(source, [new("hidden", SelectionKind.Field, "n.hidden", null, [], null)]) {
            SourceSet = source.SourceSet,
        };
        var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions()));
        await Assert.ThrowsAsync<InsightValidationException>(async () => await executor.MaterializeAsync(plan, new(), null, enforceSecurity));
    }

    public class Node { public string? Value { get; set; } public Node? Next { get; set; } [JsonIgnore] public string? Hidden { get; set; } }
    public sealed class Derived : Node { public string? Secret { get; set; } }
    public sealed class Pair { public Node? Left { get; set; } public Node? Right { get; set; } }
}
