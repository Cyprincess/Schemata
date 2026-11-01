using System.Threading.Tasks;
using Grpc.Core;
using Schemata.Insight.Grpc.Integration.Tests.Fixtures;
using Schemata.Insight.Grpc.Wire;
using Xunit;

namespace Schemata.Insight.Grpc.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class ContinuationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContinuationShould(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Continues_Same_Query_And_Rejects_Changed_Projection() {
        var request = new QueryInsightGrpcRequest {
            Sources = { new() { Alias = "b", Name = "buyers" } },
            PageSize = 1,
        };
        var first = await Query(request);
        request.PageToken = first.NextPageToken;
        var second = await Query(request);
        Assert.Single(first.Rows);
        Assert.Single(second.Rows);
        Assert.NotEqual(first.Rows[0].Fields["full_name"].StringValue, second.Rows[0].Fields["full_name"].StringValue);
        Assert.True(string.IsNullOrEmpty(second.NextPageToken));
        request.Selections.Add(new() { Field = "b.full_name" });
        var error = await Assert.ThrowsAsync<RpcException>(() => Query(request));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("AA")]
    public async Task Rejects_Invalid_Continuation_At_The_Public_Surface(string token) {
        var error = await Assert.ThrowsAsync<RpcException>(() => Query(new() {
            Sources = { new() { Alias = "b", Name = "buyers" } }, PageSize = 1, PageToken = token,
        }));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    private async Task<QueryInsightGrpcResponse> Query(QueryInsightGrpcRequest request) {
        var invoker = _factory.CreateGrpcChannel().CreateCallInvoker();
        using var call = invoker.AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), request);
        return await call.ResponseAsync;
    }
}
