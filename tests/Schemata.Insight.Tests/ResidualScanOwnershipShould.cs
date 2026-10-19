using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;
using Xunit;

using Schemata.Abstractions.Advisors;
namespace Schemata.Insight.Tests;

/// <summary>
///     The residual scan cap bounds local residual work only; a fully pushed stream pages and counts
///     through the backend contract regardless of cardinality.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ResidualScanOwnershipShould
{
    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Pages_Fully_Pushed_Rows_Beyond_The_Scan_Cap_With_An_Exact_Total() {
        var probe = new Probe { Count = 6 };
        using var provider = Services(probe);
        var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions {
            Expressions = new() { MaxResidualScanRows = 5 },
        }));

        var response = await executor.ExecuteAsync(new LimitNode(Source(), null, 3), Request(3), null,new AdviceContext(provider),  CancellationToken.None);

        Assert.Equal(Enumerable.Range(0, 3), response.Rows.Select(row => (int)row["value"]!));
        Assert.NotNull(response.NextPageToken);
        Assert.Equal(6, response.TotalSize);
        Assert.Equal(6, probe.Read);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Continues_Fully_Pushed_Later_Pages_Beyond_The_Scan_Cap_Without_Totals() {
        var probe = new Probe { Count = 12 };
        using var provider = Services(probe);
        var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions {
            Expressions = new() { MaxResidualScanRows = 5 },
            TotalSize   = Schemata.Abstractions.Resource.TotalSizeMode.None,
        }));

        var seen  = new List<int>();
        string? token = null;
        do {
            var response = await executor.ExecuteAsync(new LimitNode(Source(), null, 5), Request(5, token), null,new AdviceContext(provider),  CancellationToken.None);
            seen.AddRange(response.Rows.Select(row => (int)row["value"]!));
            Assert.Null(response.TotalSize);
            token = response.NextPageToken;
        } while (token is not null);

        Assert.Equal(Enumerable.Range(0, 12), seen);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Rejects_Local_Residual_Work_Beyond_The_Scan_Cap() {
        var probe = new Probe { Count = 6 };
        using var provider = Services(probe);
        var executor = new PlanExecutor(provider, new(provider), Options.Create(new SchemataInsightOptions {
            Expressions = new() { MaxResidualScanRows = 5 },
        }));

        var error = await Assert.ThrowsAsync<InsightValidationException>(async () => {
            await executor.ExecuteAsync(new LimitNode(new OrderNode(Source(), "value"), null, 3), Request(3), null,new AdviceContext(provider),  CancellationToken.None);
        });

        Assert.Equal(InsightReasons.InvalidArgument, error.Reason);
        Assert.Equal("5", error.Metadata!["cap"]);
        Assert.Equal(6, probe.Read);
    }

    private static QueryInsightRequest Request(int pageSize, string? token = null) {
        return new() { Sources = [new("p", "values")], PageSize = pageSize, PageToken = token };
    }

    private static SourceNode Source() {
        return new("p", new("probe", new Dictionary<string, object?>()));
    }

    private static ServiceProvider Services(Probe probe) {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddOrderExpressions();
        var result = new Mock<ISourceResult>();
        result.SetupGet(value => value.Rows).Returns(probe.Rows());
        result.SetupGet(value => value.Schema).Returns([new("value", FieldType.Int64, "p", false, [])]);
        result.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var driver = new Mock<ISourceDriver>();
        driver.SetupGet(value => value.Capabilities).Returns(DriverCapabilities.None);
        driver.Setup(value => value.ExecuteAsync(
                       It.IsAny<SubPlan>(), It.IsAny<QueryInsightRequest>(), It.IsAny<ClaimsPrincipal?>(),
                       It.IsAny<CancellationToken>()))
              .Returns(ValueTask.FromResult(result.Object));
        services.AddKeyedSingleton<ISourceDriver>("probe", driver.Object);
        return services.BuildServiceProvider();
    }

    private sealed class Probe
    {
        internal int Count { get; init; } = 6;

        internal int Read { get; private set; }

        internal async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows(
            [EnumeratorCancellation] CancellationToken ct = default
        ) {
            for (var index = 0; index < Count; index++) {
                ct.ThrowIfCancellationRequested();
                Read++;
                yield return new Dictionary<string, object?> { ["value"] = index };
                await Task.Yield();
            }
        }
    }
}
