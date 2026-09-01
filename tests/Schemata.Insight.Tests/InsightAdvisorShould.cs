using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Xunit;

namespace Schemata.Insight.Tests;

/// <summary>
///     Covers the wiring of the Insight advisor contracts into the query pipeline: request and
///     response aspects wrap the dispatch as
///     <see cref="IRequestPipelineAdvisor{TRequest,TResponse}" /> of
///     <see cref="QueryInsightRequest" />/<see cref="QueryInsightResponse" /> (a request advisor runs
///     before the handler and rejects by throwing; a response advisor runs after it and may redact
///     the response in place), plan advisors may rewrite the plan the executor consumes, source
///     advisors may block a source before it is opened, and same-typed wrap advisors run in ascending
///     <see cref="IAdvisor.Order" />.
/// </summary>
public sealed class InsightAdvisorShould
{
    private const string DriverName = "probe";

    [Fact]
    public async Task RequestAdvisor_Throw_Propagates_Unwrapped_From_QueryAsync() {
        var driver = new Mock<ISourceDriver>(MockBehavior.Strict);
        driver.SetupGet(current => current.Capabilities).Returns(DriverCapabilities.None);

        var thrown  = new ProbeException("rejected by request advisor");
        var advisor = new RequestAdvisor((_, _, _) => throw thrown);

        await using var provider = CreateProvider(driver.Object, requestAdvisors: [advisor]);
        var insight = provider.GetRequiredService<IInsightService>();

        var exception = await Assert.ThrowsAsync<ProbeException>(
            async () => await insight.QueryAsync(Request(), null));

        Assert.Same(thrown, exception);
        driver.Verify(current => current.ExecuteAsync(
                           It.IsAny<SubPlan>(), It.IsAny<QueryInsightRequest>(), It.IsAny<ClaimsPrincipal?>(),
                           It.IsAny<CancellationToken>()),
                       Times.Never);
    }

    [Fact]
    public async Task PlanAdvisor_RewrittenPlan_Is_What_The_Executor_Consumes() {
        var driver = CreateDriver(ValueRows(10));
        var advisor = new PlanAdvisor((plan, _, _)
            => ValueTask.FromResult(plan is LimitNode limit ? limit with { Take = 3 } : plan));

        await using var provider = CreateProvider(driver.Object, planAdvisors: [advisor]);
        var insight = provider.GetRequiredService<IInsightService>();

        var response = await insight.QueryAsync(Request(pageSize: 250), null);

        // The request asked for a page of 250; only the plan advisor's rewritten Take of 3 explains 3 rows.
        Assert.Equal(3, response.Rows.Count);
        Assert.NotNull(response.NextPageToken);
    }

    [Fact]
    public async Task SourceAdvisor_Throw_Blocks_The_Source_And_Propagates_Unwrapped() {
        var driver = new Mock<ISourceDriver>(MockBehavior.Strict);
        driver.SetupGet(current => current.Capabilities).Returns(DriverCapabilities.None);

        var thrown = new ProbeException("blocked by source advisor");
        SourceBinding? received = null;
        SourceConfig?  receivedConfig = null;
        var advisor = new SourceAdvisor((binding, config, _, _) => {
            received       = binding;
            receivedConfig = config;
            throw thrown;
        });

        await using var provider = CreateProvider(driver.Object, sourceAdvisors: [advisor]);
        var insight = provider.GetRequiredService<IInsightService>();

        var exception = await Assert.ThrowsAsync<ProbeException>(
            async () => await insight.QueryAsync(Request(), null));

        Assert.Same(thrown, exception);
        Assert.Equal("source", received?.Alias);
        Assert.Equal("orders", received?.Name);
        Assert.Equal(DriverName, receivedConfig?.DriverName);
        driver.Verify(current => current.ExecuteAsync(
                           It.IsAny<SubPlan>(), It.IsAny<QueryInsightRequest>(), It.IsAny<ClaimsPrincipal?>(),
                           It.IsAny<CancellationToken>()),
                       Times.Never);
    }

    [Fact]
    public async Task SourceAdvisor_Registered_Scoped_Runs_Against_The_Request_Scope() {
        // PlanExecutor is itself scoped so a scoped IInsightSourceAdvisor resolves against the same
        // request scope the handler runs in, rather than against the root provider. With
        // ValidateScopes enabled, resolving a scoped service from the root provider throws, so this
        // regresses the captive-dependency bug rather than merely re-covering the strict-throw path.
        var driver = CreateDriver(ValueRows(1));
        var ran     = false;
        var advisor = new SourceAdvisor((_, _, _, _) => {
            ran = true;
            return ValueTask.CompletedTask;
        });

        var services = new ServiceCollection();
        services.Configure<SchemataInsightOptions>(options =>
            options.Sources["orders"] = new(DriverName, new Dictionary<string, object?>()));
        services.AddKeyedSingleton<ISourceDriver>(DriverName, driver.Object);
        services.AddScoped<IInsightSourceAdvisor>(_ => advisor);
        services.AddSchemataInsight();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var insight = provider.GetRequiredService<IInsightService>();

        var response = await insight.QueryAsync(Request(), null);

        Assert.True(ran);
        Assert.Single(response.Rows);
    }

    [Fact]
    public async Task ResponseAdvisor_Redacts_Response_InPlace_And_Caller_Sees_It() {
        var driver = CreateDriver(ValueRows(1));
        var advisor = new ResponseAdvisor((response, _, _) => {
            for (var i = 0; i < response.Rows.Count; i++) {
                response.Rows[i] = new Dictionary<string, object?>(response.Rows[i]) { ["value"] = "REDACTED" };
            }

            return ValueTask.CompletedTask;
        });

        await using var provider = CreateProvider(driver.Object, responseAdvisors: [advisor]);
        var insight = provider.GetRequiredService<IInsightService>();

        var response = await insight.QueryAsync(Request(), null);

        Assert.Equal("REDACTED", Assert.Single(response.Rows)["value"]);
    }

    [Fact]
    public async Task Multiple_Advisors_Of_The_Same_Type_Run_In_Registration_Order() {
        var driver = CreateDriver(ValueRows(1));
        var order  = new List<string>();
        var first  = new RequestAdvisor((_, _, _) => {
            order.Add("first");
            return ValueTask.CompletedTask;
        });
        var second = new RequestAdvisor((_, _, _) => {
            order.Add("second");
            return ValueTask.CompletedTask;
        });

        await using var provider = CreateProvider(driver.Object, requestAdvisors: [first, second]);
        var insight = provider.GetRequiredService<IInsightService>();

        await insight.QueryAsync(Request(), null);

        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task Advisors_Run_In_Ascending_Order_Not_Registration_Order() {
        var driver = CreateDriver(ValueRows(1));
        var order  = new List<string>();
        // Registered high-Order first, low-Order second: Order, not registration, must decide.
        var late  = new OrderedRequestAdvisor(20, "late", order);
        var early = new OrderedRequestAdvisor(10, "early", order);

        await using var provider = CreateProvider(driver.Object, requestAdvisors: [late, early]);
        var insight = provider.GetRequiredService<IInsightService>();

        await insight.QueryAsync(Request(), null);

        Assert.Equal(["early", "late"], order);
    }

    private static QueryInsightRequest Request(int pageSize = 25) {
        return new() { Sources = [new("source", "orders")], PageSize = pageSize };
    }

    private static ServiceProvider CreateProvider(
        ISourceDriver                                                    driver,
        IEnumerable<IRequestPipelineAdvisor<QueryInsightRequest, QueryInsightResponse>>? requestAdvisors = null,
        IEnumerable<IInsightPlanAdvisor>?                                planAdvisors = null,
        IEnumerable<IInsightSourceAdvisor>?                              sourceAdvisors = null,
        IEnumerable<IRequestPipelineAdvisor<QueryInsightRequest, QueryInsightResponse>>? responseAdvisors = null
    ) {
        var services = new ServiceCollection();
        services.Configure<SchemataInsightOptions>(options =>
            options.Sources["orders"] = new(DriverName, new Dictionary<string, object?>()));
        services.AddKeyedSingleton<ISourceDriver>(DriverName, driver);

        foreach (var advisor in requestAdvisors ?? []) {
            services.AddSingleton(advisor);
        }

        foreach (var advisor in planAdvisors ?? []) {
            services.AddSingleton(advisor);
        }

        foreach (var advisor in sourceAdvisors ?? []) {
            services.AddSingleton(advisor);
        }

        foreach (var advisor in responseAdvisors ?? []) {
            services.AddSingleton(advisor);
        }

        services.AddSchemataInsight();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static Mock<ISourceDriver> CreateDriver(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) {
        var result = new Mock<ISourceResult>(MockBehavior.Strict);
        result.SetupGet(current => current.Rows).Returns(Stream(rows));
        result.SetupGet(current => current.Schema).Returns([new("value", FieldType.Int64, "source", false, [])]);
        result.Setup(current => current.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var driver = new Mock<ISourceDriver>(MockBehavior.Strict);
        driver.SetupGet(current => current.Capabilities).Returns(DriverCapabilities.None);
        driver.Setup(current => current.ExecuteAsync(
                          It.IsAny<SubPlan>(), It.IsAny<QueryInsightRequest>(), It.IsAny<ClaimsPrincipal?>(),
                          It.IsAny<CancellationToken>()))
              .Returns(ValueTask.FromResult(result.Object));

        return driver;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ValueRows(int count) {
        var rows = new List<IReadOnlyDictionary<string, object?>>(count);
        for (var i = 0; i < count; i++) {
            rows.Add(new Dictionary<string, object?> { ["value"] = i });
        }

        return rows;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Stream(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows
    ) {
        foreach (var row in rows) {
            yield return row;
            await Task.Yield();
        }
    }

    private sealed class ProbeException(string message) : Exception(message);

    private sealed class RequestAdvisor(Func<QueryInsightRequest, ClaimsPrincipal?, CancellationToken, ValueTask> advise)
        : IRequestPipelineAdvisor<QueryInsightRequest, QueryInsightResponse>
    {
        public int Order => 0;

        public async Task<QueryInsightResponse> AdviseAsync(
            AdviceContext                                    ctx,
            QueryInsightRequest                              request,
            RequestHandlerContinuation<QueryInsightResponse> next,
            CancellationToken                                ct = default
        ) {
            await advise(request, request.Principal, ct);
            return await next(ct);
        }
    }

    private sealed class OrderedRequestAdvisor(int order, string tag, List<string> trail)
        : IRequestPipelineAdvisor<QueryInsightRequest, QueryInsightResponse>
    {
        public int Order => order;

        public async Task<QueryInsightResponse> AdviseAsync(
            AdviceContext                                    ctx,
            QueryInsightRequest                              request,
            RequestHandlerContinuation<QueryInsightResponse> next,
            CancellationToken                                ct = default
        ) {
            trail.Add(tag);
            return await next(ct);
        }
    }

    private sealed class PlanAdvisor(Func<PlanNode, QueryInsightRequest, CancellationToken, ValueTask<PlanNode>> advise)
        : IInsightPlanAdvisor
    {
        public int Order => 0;

        public async Task<AdviseResult> AdviseAsync(
            AdviceContext       ctx,
            QueryInsightRequest request,
            CancellationToken   ct = default
        ) {
            var plan = ctx.Get<PlanNode>()!;
            ctx.Set(await advise(plan, request, ct));
            return AdviseResult.Continue;
        }
    }

    private sealed class SourceAdvisor(
        Func<SourceBinding, SourceConfig, ClaimsPrincipal?, CancellationToken, ValueTask> advise
    ) : IInsightSourceAdvisor
    {
        public int Order => 0;

        public async Task<AdviseResult> AdviseAsync(
            AdviceContext     ctx,
            SourceBinding     binding,
            SourceConfig      config,
            ClaimsPrincipal?  principal,
            CancellationToken ct = default
        ) {
            await advise(binding, config, principal, ct);
            return AdviseResult.Continue;
        }
    }

    private sealed class ResponseAdvisor(Func<QueryInsightResponse, QueryInsightRequest, CancellationToken, ValueTask> advise)
        : IRequestPipelineAdvisor<QueryInsightRequest, QueryInsightResponse>
    {
        public int Order => 0;

        public async Task<QueryInsightResponse> AdviseAsync(
            AdviceContext                                    ctx,
            QueryInsightRequest                              request,
            RequestHandlerContinuation<QueryInsightResponse> next,
            CancellationToken                                ct = default
        ) {
            var response = await next(ct);
            await advise(response, request, ct);
            return response;
        }
    }
}
