using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;
using Xunit;

namespace Schemata.Insight.Tests;

public sealed class InsightFoundationShould
{
    [Fact]
    public async Task QueryAsync_With_Foundation_Registration_Executes_Federated_Query_With_Caller_Principal() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));
        ClaimsPrincipal? receivedPrincipal = null;
        QueryInsightRequest? receivedRequest = null;

        var result = new Mock<ISourceResult>(MockBehavior.Strict);
        result.SetupGet(current => current.Rows).Returns(Rows());
        result.SetupGet(current => current.Schema).Returns([new("value", FieldType.Int64, "source", false, [])]);
        result.Setup(current => current.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var driver = new Mock<ISourceDriver>(MockBehavior.Strict);
        driver.SetupGet(current => current.Capabilities).Returns(DriverCapabilities.None);
        driver.Setup(current => current.ExecuteAsync(
                        It.IsAny<SubPlan>(),
                        It.IsAny<QueryInsightRequest>(),
                        It.IsAny<ClaimsPrincipal?>(),
                        It.IsAny<CancellationToken>()))
              .Returns((SubPlan _, QueryInsightRequest request, ClaimsPrincipal? caller, CancellationToken _) => {
                  receivedRequest   = request;
                  receivedPrincipal = caller;
                  return ValueTask.FromResult(result.Object);
              });

        var services = new ServiceCollection();
        services.Configure<SchemataInsightOptions>(options =>
            options.Sources["orders"] = new("probe", new Dictionary<string, object?>()));
        services.AddKeyedSingleton<ISourceDriver>("probe", driver.Object);
        services.AddSchemataInsight();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var insight = provider.GetRequiredService<IInsightService>();
        var response = await insight.QueryAsync(new() { Sources = [new("source", "orders")] }, principal);

        Assert.Equal(42, Assert.Single(response.Rows)["value"]);
        Assert.Same(principal, receivedPrincipal);
        Assert.Same(principal, receivedRequest?.Principal);
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows() {
        yield return new Dictionary<string, object?> { ["value"] = 42 };
        await Task.CompletedTask;
    }
}
