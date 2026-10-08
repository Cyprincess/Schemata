using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Xunit;

namespace Schemata.Insight.Tests;

/// <summary>
///     Proves the facade (<see cref="DefaultInsightService.QueryAsync" />, which itself dispatches
///     through <see cref="IRequestDispatcher" />) and a raw <see cref="IRequestDispatcher" /> entry run
///     the exact same <see cref="QueryInsightRequest" /> pipeline: equivalent
///     <see cref="QueryInsightResponse" /> results, the registered <see cref="IRequestPipelineAdvisor{TRequest,TResponse}" />
///     firing once per entry, and identical exception shapes when the request is malformed. Neither
///     entry stubs the real <c>DefaultQueryInsightHandler</c>.
/// </summary>
public sealed class InsightEntryEquivalenceShould
{
    private const string DriverName = "equivalence-probe";

    [Fact]
    public async Task Query_Through_Facade_And_Dispatcher_Produce_Equivalent_Responses_And_Fire_The_Same_Advisor() {
        var facadeSpy = new RecordingQueryAdvisor();
        await using var facadeProvider = CreateProvider(CreateDriver(ValueRows(3)).Object, facadeSpy);
        var insight = facadeProvider.GetRequiredService<IInsightService>();
        var facadeResponse = await insight.QueryAsync(Request(), null);

        var dispatcherSpy = new RecordingQueryAdvisor();
        await using var dispatcherProvider = CreateProvider(CreateDriver(ValueRows(3)).Object, dispatcherSpy);
        using var dispatcherScope = dispatcherProvider.CreateScope();
        var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var dispatcherRequest = Request();
        dispatcherRequest.Principal = null;
        var dispatcherResponse = await dispatcher.SendAsync<QueryInsightRequest, QueryInsightResponse>(
            dispatcherRequest, CancellationToken.None);

        // Full response shape, not just row count: the actual row contents, the schema, and every
        // pagination/partial-response field must match between entries.
        Assert.Equal(
            facadeResponse.Rows.Select(row => row["value"]),
            dispatcherResponse.Rows.Select(row => row["value"]));
        Assert.Equal(
            facadeResponse.Schema.Select(field => (field.Name, field.Type, field.SourceAlias, field.IsList)),
            dispatcherResponse.Schema.Select(field => (field.Name, field.Type, field.SourceAlias, field.IsList)));
        Assert.Equal(facadeResponse.NextPageToken, dispatcherResponse.NextPageToken);
        Assert.Equal(facadeResponse.TotalSize, dispatcherResponse.TotalSize);
        Assert.Equal(facadeResponse.Unreachable, dispatcherResponse.Unreachable);
        Assert.Equal(1, facadeSpy.Count);
        Assert.Equal(1, dispatcherSpy.Count);
    }

    [Fact]
    public async Task Query_Throw_The_Same_Exception_Payload_Through_Both_Entries_For_A_Sourceless_Request() {
        await using var facadeProvider = CreateProvider(CreateDriver(ValueRows(0)).Object, advisor: null);
        var insight = facadeProvider.GetRequiredService<IInsightService>();
        var facadeException = await Record.ExceptionAsync(() => insight.QueryAsync(new(), null).AsTask());

        await using var dispatcherProvider = CreateProvider(CreateDriver(ValueRows(0)).Object, advisor: null);
        using var dispatcherScope = dispatcherProvider.CreateScope();
        var dispatcher = dispatcherScope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var dispatcherException = await Record.ExceptionAsync(() => dispatcher.SendAsync<QueryInsightRequest, QueryInsightResponse>(
            new(), CancellationToken.None));

        var facadeValidation     = Assert.IsType<InsightValidationException>(facadeException);
        var dispatcherValidation = Assert.IsType<InsightValidationException>(dispatcherException);

        Assert.Equal(facadeValidation.Reason, dispatcherValidation.Reason);
        Assert.Equal(facadeValidation.Message, dispatcherValidation.Message);
        Assert.Equal(facadeValidation.Metadata, dispatcherValidation.Metadata);
    }

    private static QueryInsightRequest Request(int pageSize = 25) {
        return new() { Sources = [new("source", "orders")], PageSize = pageSize };
    }

    private static ServiceProvider CreateProvider(ISourceDriver driver, IRequestPipelineAdvisor<QueryInsightRequest, QueryInsightResponse>? advisor) {
        var services = new ServiceCollection();
        services.Configure<SchemataInsightOptions>(options =>
            options.Sources["orders"] = new(DriverName, new Dictionary<string, object?>()));
        services.AddKeyedSingleton<ISourceDriver>(DriverName, driver);
        if (advisor is not null) {
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

    /// <summary>Records every dispatch of <see cref="QueryInsightRequest" /> it observes.</summary>
    private sealed class RecordingQueryAdvisor : IRequestPipelineAdvisor<QueryInsightRequest, QueryInsightResponse>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<QueryInsightResponse> AdviseAsync(
            AdviceContext                                    ctx,
            QueryInsightRequest                              a1,
            RequestHandlerContinuation<QueryInsightResponse> next,
            CancellationToken                                ct = default) {
            Count++;
            return next(ct);
        }
    }
}
