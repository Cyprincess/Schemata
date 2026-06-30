using System.Threading.Tasks;
using Grpc.Core;
using Schemata.Insight.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Insight.Grpc.Integration.Tests;

[Trait("Category", "Integration")]
public class InsightGrpcIntegrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public InsightGrpcIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Query_AllBuyers_ReturnsDynamicRows() {
        var response = await Query(new() {
            Sources = { new() { Alias = "b", Name = "buyers" } },
        });

        Assert.Equal(2, response.Rows.Count);
        Assert.Equal(2, response.TotalSize);
        Assert.Contains(response.Rows, row => row.Fields["full_name"].StringValue == "Ada");
        Assert.Contains(response.Rows, row => row.Fields["full_name"].StringValue == "Bob");
    }

    [Fact]
    public async Task Query_FilteredAndPaged_ProjectsAndPaginates() {
        var response = await Query(new() {
            Sources         = { new() { Alias = "b", Name = "buyers" } },
            Transformations = { new() { Filter = new() { Source = "id > 1", Language = "cel" } } },
            Selections      = { new() { Field = "b.full_name", Alias = "full_name" } },
            PageSize        = 1,
        });

        Assert.Single(response.Rows);
        Assert.Equal("Bob", response.Rows[0].Fields["full_name"].StringValue);
    }

    [Fact]
    public async Task Query_UnknownSource_FailsWithNotFound() {
        var ex = await Assert.ThrowsAsync<RpcException>(() => Query(new() {
            Sources = { new() { Alias = "x", Name = "missing" } },
        }));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    private async Task<QueryInsightGrpcResponse> Query(QueryInsightGrpcRequest request) {
        var invoker = _factory.CreateGrpcChannel().CreateCallInvoker();
        using var call = invoker.AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), request);
        return await call.ResponseAsync;
    }
}
