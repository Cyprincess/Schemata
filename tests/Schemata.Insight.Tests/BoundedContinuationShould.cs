using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Tenancy;
using Schemata.Common;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;
using Xunit;

namespace Schemata.Insight.Tests;

[Trait("Category", "Integration")]
public sealed class BoundedContinuationShould
{
    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData("order")]
    [InlineData("group")]
    [InlineData("estimated")]
    public async Task Stops_And_Disposes_Source_At_Scan_Budget(string stage) {
        var probe = new ScanProbe();
        using var provider = Services(probe, 4);
        var local = new LocalPipelineExecutor(provider);
        var source = Source();
        PlanNode plan = stage == "order" ? new OrderNode(source, "value")
            : new GroupNode(source, ["p.value"], [new("count", AggregationFunction.Count, "p.value")]);
        var options = new SchemataInsightOptions {
            MaxResidualScanRows = 4,
            TotalSize = stage == "estimated" ? Schemata.Abstractions.Resource.TotalSizeMode.Estimated : Schemata.Abstractions.Resource.TotalSizeMode.Exact,
        };
        var executor = new PlanExecutor(provider, local, Options.Create(options));

        var error = await Assert.ThrowsAsync<InsightValidationException>(async () =>
            await executor.ExecuteAsync(plan, new(), null, CancellationToken.None));

        Assert.Equal("INVALID_ARGUMENT", error.Status);
        Assert.Equal(5, probe.Read);
        Assert.True(probe.Disposed);
    }

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData("order")]
    [InlineData("group")]
    public async Task Bounds_Local_Materialization_Before_Allocating_The_Complete_Source(string stage) {
        var probe = new ScanProbe();
        using var provider = Services(probe, 4);
        PlanNode plan = stage == "order" ? new OrderNode(Source(), "value")
            : new GroupNode(Source(), ["value"], [new("count", AggregationFunction.Count, "value")]);
        var local = new LocalPipelineExecutor(provider);
        var error = await Assert.ThrowsAsync<InsightValidationException>(async () => {
            await foreach (var row in local.RunStagesAsync(probe.Rows(), [plan], CancellationToken.None)) {
                Assert.Fail("An overflowing materialization cannot yield a partial result.");
            }
        });
        Assert.Equal("INVALID_ARGUMENT", error.Status);
        Assert.Equal(5, probe.Read);
        Assert.True(probe.Disposed);
    }

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Accepts_Complete_Sources_Through_The_Exact_Budget(int count) {
        var probe = new ScanProbe { Count = count };
        using var provider = Services(probe, 4);
        var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions { MaxResidualScanRows = 4 }));
        var response = await executor.ExecuteAsync(Source(), new(), null, CancellationToken.None);
        Assert.Equal(Enumerable.Range(0, count), response.Rows.Select(row => (int)row["value"]!));
        Assert.Equal(count, response.TotalSize);
        Assert.Equal(count, probe.Read);
        Assert.True(probe.Disposed);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Disposes_Raw_Source_When_Cancelled_During_Buffering() {
        using var cancellation = new CancellationTokenSource();
        var probe = new ScanProbe { OnRead = count => { if (count == 2) cancellation.Cancel(); } };
        using var provider = Services(probe, 4);
        var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions { MaxResidualScanRows = 4 }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await executor.ExecuteAsync(
            new GroupNode(Source(), ["p.value"], []), new(), null, cancellation.Token));
        Assert.Equal(2, probe.Read);
        Assert.True(probe.Disposed);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Groups_Values_By_Type_And_Structure_Without_Delimiter_Collisions() {
        IReadOnlyDictionary<string, object?>[] rows = [
            Row(null, "x"), Row("\0", "x"), Row(1, "x"), Row("1", "x"),
            Row("a\u001fb", "c"), Row("a", "b\u001fc"),
            Row(new List<object?> { 1, "x" }, null), Row(new List<object?> { 1, "x" }, null),
            Row(new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x" }, null),
            Row(new Dictionary<string, object?> { ["b"] = "x", ["a"] = 1 }, null),
            Row(new byte[] { 1, 2 }, null), Row(new byte[] { 1, 2 }, null),
        ];
        using var provider = new ServiceCollection().BuildServiceProvider();
        var local = new LocalPipelineExecutor(provider);
        var plan = new GroupNode(Source(), ["first", "second"], [new("count", AggregationFunction.Count, "first")]);
        var grouped = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in local.RunStagesAsync(Rows(rows), [plan], CancellationToken.None)) grouped.Add(row);
        Assert.Equal(9, grouped.Count);
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1, 2, 2, 2 }, grouped.Select(row => (int)row["count"]!));
        Assert.Null(grouped[0]["first"]);
        Assert.Equal("\0", grouped[1]["first"]);
        Assert.IsType<int>(grouped[2]["first"]);
        Assert.IsType<string>(grouped[3]["first"]);
    }

    [Theory]
    [Trait("Layer", "Unit")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Groups_Map_Keys_Ordinally_Independent_Of_Lookup_Comparer_And_Input_Order(bool reverse) {
        IReadOnlyDictionary<string, object?>[] maps = [
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = 1, ["nullable"] = null },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["nullable"] = null, ["a"] = 1 },
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1, ["nullable"] = null },
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["nullable"] = null, ["A"] = 1 },
        ];
        if (reverse) Array.Reverse(maps);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var local = new LocalPipelineExecutor(provider);
        var plan = new GroupNode(Source(), ["first"], [new("count", AggregationFunction.Count, "first")]);
        var grouped = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in local.RunStagesAsync(Rows(maps.Select(map => Row(map, null))), [plan], CancellationToken.None)) grouped.Add(row);
        Assert.Equal(2, grouped.Count);
        for (var index = 0; index < grouped.Count; index++) {
            var row = grouped[index];
            Assert.Equal(2, row["count"]);
            var map = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row["first"]);
            var expected = (index == 0) == reverse ? "A" : "a";
            Assert.Equal(new[] { expected, "nullable" }, map.Keys.OrderBy(key => key, StringComparer.Ordinal));
            Assert.Null(map["nullable"]);
        }
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Continues_After_Fresh_Provider_Restart_With_The_Same_Keyring() {
        var directory = Directory.CreateTempSubdirectory("schemata-insight-pages-");
        try {
            string? token = null;
            var actual = new List<int>();
            do {
                using var provider = Services(new() { Count = 7 }, 20, directory);
                var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions()));
                var request = Request(token);
                var response = await executor.ExecuteAsync(new LimitNode(Source(), 0, 2), request, Principal("alice"), CancellationToken.None);
                actual.AddRange(response.Rows.Select(row => (int)row["value"]!));
                token = response.NextPageToken;
            } while (token is not null);
            Assert.Equal(Enumerable.Range(0, 7), actual);
        } finally { directory.Delete(true); }
    }

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData("source")]
    [InlineData("selection")]
    [InlineData("language")]
    [InlineData("page")]
    [InlineData("transformation")]
    [InlineData("skip")]
    [InlineData("subject")]
    [InlineData("tenant")]
    [InlineData("tamper")]
    [InlineData("truncate")]
    [InlineData("keyring")]
    public async Task Rejects_Tokens_Outside_Their_Query_And_Caller_Binding(string change) {
        var directory = Directory.CreateTempSubdirectory("schemata-insight-binding-");
        var other = Directory.CreateTempSubdirectory("schemata-insight-other-");
        try {
            using var provider = Services(new() { Count = 7 }, 20, directory);
            var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions()));
            var first = await executor.ExecuteAsync(new LimitNode(Source(), 0, 2), Request(), Principal("alice"), CancellationToken.None);
            var token = first.NextPageToken!;
            var request = Request(token);
            if (change == "source") request.Sources = [new("p", "other")];
            if (change == "selection") request.Selections = [new() { Field = "p.value" }];
            if (change == "language") request.Language = "other";
            if (change == "transformation") request.Transformations = [new() { OrderBy = new("value") }];
            if (change == "skip") request.Skip = 1;
            if (change == "tamper") request.PageToken = (token[0] == 'A' ? "B" : "A") + token[1..];
            if (change == "truncate") request.PageToken = token[..(token.Length / 2)];
            using var tenant = TenantContext.Enter(change == "tenant" ? new(Guid.Parse("00000000-0000-0000-0000-000000000001")) : TenantIdentity.Host);
            using var second = Services(new() { Count = 7 }, 20, change == "keyring" ? other : directory);
            var resumed = new PlanExecutor(second, new(second), Options.Create(new SchemataInsightOptions()));
            var error = await Assert.ThrowsAsync<InsightValidationException>(async () => await resumed.ExecuteAsync(
                new LimitNode(Source(), change == "skip" ? 1 : 0, change == "page" ? 3 : 2), request,
                Principal(change == "subject" ? "bob" : "alice"), CancellationToken.None));
            Assert.Equal("INVALID_ARGUMENT", error.Status);
        } finally { directory.Delete(true); other.Delete(true); }
    }

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Rejects_Negative_And_Overflowing_Request_Offsets(int skip) {
        var probe = new ScanProbe();
        using var provider = Services(probe, 20);
        var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions()));
        var error = await Assert.ThrowsAsync<InsightValidationException>(async () => await executor.ExecuteAsync(
            new LimitNode(Source(), skip, 2), Request(), null, CancellationToken.None));
        Assert.Equal("INVALID_ARGUMENT", error.Status);
        Assert.Equal(0, probe.Read);
    }

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Rejects_Protected_Negative_And_Overflowing_Continuation_Offsets(int skip) {
        using var provider = Services(new() { Count = 7 }, 20);
        var request = Request();
        var options = new SchemataInsightOptions();
        var binding = System.Text.Json.JsonSerializer.Serialize(new {
            request.Sources, request.Joins, request.Transformations, request.Selections, request.Language,
            Skip = 0, PageSize = 2, options.TotalSize, options.MaxResidualScanRows,
        }, SchemataJson.Default);
        request.PageToken = ProtectedContinuation.Encode(provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Schemata.Insight.Foundation.PageToken"), new { Binding = binding, Tenant = (Guid?)null,
                Caller = (ProtectedContinuationCaller?)default(ProtectedContinuationCaller), Skip = skip });
        var executor = new PlanExecutor(provider, new(provider), Options.Create(options));
        var error = await Assert.ThrowsAsync<InsightValidationException>(async () => await executor.ExecuteAsync(
            new LimitNode(Source(), 0, 2), request, null, CancellationToken.None));
        Assert.Equal("INVALID_ARGUMENT", error.Status);
    }

    private static QueryInsightRequest Request(string? token = null) => new() { Sources = [new("p", "values")], PageSize = 2, PageToken = token };
    private static ClaimsPrincipal Principal(string subject) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], "test"));
    private static SourceNode Source() => new("p", new("probe", new Dictionary<string, object?>()));
    private static IReadOnlyDictionary<string, object?> Row(object? first, object? second) => new Dictionary<string, object?> { ["first"] = first, ["second"] = second };

    private static ServiceProvider Services(ScanProbe probe, int cap, DirectoryInfo? keys = null) {
        var services = new ServiceCollection();
        var protection = services.AddDataProtection();
        if (keys is not null) protection.PersistKeysToFileSystem(keys).SetApplicationName("InsightContinuationTests");
        services.Configure<SchemataInsightOptions>(options => options.MaxResidualScanRows = cap);
        var result = new Mock<ISourceResult>();
        result.SetupGet(value => value.Rows).Returns(probe.Rows());
        result.SetupGet(value => value.Schema).Returns([new("value", FieldType.Int64, "p", false, [])]);
        result.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var driver = new Mock<ISourceDriver>();
        driver.SetupGet(value => value.Capabilities).Returns(DriverCapabilities.None);
        driver.Setup(value => value.ExecuteAsync(It.IsAny<SubPlan>(), It.IsAny<QueryInsightRequest>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(result.Object));
        services.AddKeyedSingleton<ISourceDriver>("probe", driver.Object);
        var order = new Mock<IOrderCompiler>();
        order.Setup(value => value.Parse("value")).Returns([new(["p", "value"], false)]);
        services.AddSingleton(order.Object);
        return services.BuildServiceProvider();
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows(IEnumerable<IReadOnlyDictionary<string, object?>> rows) {
        foreach (var row in rows) { yield return row; await Task.Yield(); }
    }

    private sealed class ScanProbe
    {
        internal int Count { get; init; } = 20;
        internal int Read { get; private set; }
        internal bool Disposed { get; private set; }
        internal Action<int>? OnRead { get; init; }
        internal async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows([EnumeratorCancellation] CancellationToken ct = default) {
            try {
                for (var index = 0; index < Count; index++) {
                    ct.ThrowIfCancellationRequested();
                    Read++;
                    OnRead?.Invoke(Read);
                    yield return new Dictionary<string, object?> { ["value"] = index };
                    await Task.Yield();
                }
            } finally { Disposed = true; }
        }
    }
}
