using System.Linq;
using System.Threading.Tasks;
using Schemata.Insight.Grpc.Integration.Tests.Fixtures;
using Schemata.Insight.Grpc.Wire;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Insight.Grpc.Integration.Tests;

[Trait("Layer", "Integration")]
public class LinkHeaderShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task Query_Projects_FullRpc_And_Replayable_Continuation() {
        using var channel = factory.CreateGrpcChannel();
        var invoker = channel.CreateCallInvoker();
        var request = new QueryInsightGrpcRequest { Sources = { new() { Alias = "b", Name = "buyers" } }, PageSize = 1 };
        using var first = invoker.AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), request);
        var response = await first.ResponseAsync;
        var links = LinkHeader.Parse(first.GetTrailers().Single(entry => entry.Key == "link").Value);
        Assert.Equal(new[] { "self", "next" }, links.Select(link => link.Rel));
        Assert.All(links, link => Assert.Equal(InsightGrpcMethods.Query.FullName, link.Target));
        var token = links.Single(link => link.Rel == "next").Extensions!["page_token"];
        Assert.Equal(response.NextPageToken, token);
        request.PageToken = token;
        using var second = invoker.AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), request);
        var secondResponse = await second.ResponseAsync;
        Assert.NotEqual(response.Rows[0].Fields["full_name"].StringValue, secondResponse.Rows[0].Fields["full_name"].StringValue);
        var previous = Assert.Single(LinkHeader.Parse(second.GetTrailers().Single(entry => entry.Key == "link").Value), link => link.Rel == "prev");
        request.PageToken = previous.Extensions!["page_token"];
        using var back = invoker.AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), request);
        Assert.Equal(response.Rows[0].Fields["full_name"].StringValue, (await back.ResponseAsync).Rows[0].Fields["full_name"].StringValue);
    }
}
