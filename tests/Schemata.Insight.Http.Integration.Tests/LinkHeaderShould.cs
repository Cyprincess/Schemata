using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Insight.Http.Integration.Tests.Fixtures;
using Schemata.Abstractions.Linking;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Insight.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class LinkHeaderShould : IClassFixture<WebAppFactory>
{
    private const string Body =
        """
        {
            "sources": [{ "alias": "s", "name": "students" }],
            "transformations": [{ "order_by": { "order_by": "age asc" } }],
            "selections": [{ "field": "s.full_name" }],
            "page_size": 1
        }
        """;

    private const string Windowed =
        """
        {
            "sources": [{ "alias": "s", "name": "students" }],
            "transformations": [{ "order_by": { "order_by": "age asc" } }],
            "selections": [{ "field": "s.full_name" }],
            "skip": 1,
            "page_size": 1
        }
        """;

    private readonly WebAppFactory _factory;

    public LinkHeaderShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Paginated_Query_Emits_Replayable_Next_And_Prev_Tokens() {
        var client = _factory.CreateClient();

        var first = await Query(client, Body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstLinks = Links(first);
        Assert.Equal("/v1/insight:query", Path(Assert.Single(firstLinks, link => link.Rel == "self").Target));
        var next = Assert.Single(firstLinks, link => link.Rel == "next");
        Assert.Equal("/v1/insight:query", Path(next.Target));
        Assert.NotNull(next.Extensions);
        Assert.True(next.Extensions!.ContainsKey("page_token"));
        Assert.DoesNotContain(firstLinks, link => link.Rel == "prev");

        var second = await Query(client, WithToken(Body, next.Extensions!["page_token"]));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Cleo", await FirstName(second));
        var secondLinks = Links(second);
        Assert.Equal("/v1/insight:query", Path(Assert.Single(secondLinks, link => link.Rel == "self").Target));
        var secondNext  = Assert.Single(secondLinks, link => link.Rel == "next");
        var previous    = Assert.Single(secondLinks, link => link.Rel == "prev");
        Assert.Equal("/v1/insight:query", Path(secondNext.Target));
        Assert.Equal("/v1/insight:query", Path(previous.Target));
        Assert.NotNull(previous.Extensions);
        Assert.True(previous.Extensions!.ContainsKey("page_token"));
        Assert.NotEqual(secondNext.Extensions!["page_token"], previous.Extensions["page_token"]);

        var forward = await Query(client, WithToken(Body, secondNext.Extensions["page_token"]));
        Assert.Equal(HttpStatusCode.OK, forward.StatusCode);
        Assert.Equal("Ada", await FirstName(forward));
        var forwardLinks = Links(forward);
        Assert.DoesNotContain(forwardLinks, link => link.Rel == "next");
        var finalPrevious = Assert.Single(forwardLinks, link => link.Rel == "prev");
        Assert.NotNull(finalPrevious.Extensions);
        Assert.True(finalPrevious.Extensions!.ContainsKey("page_token"));

        var replayed = await Query(client, WithToken(Body, finalPrevious.Extensions["page_token"]));
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal("Cleo", await FirstName(replayed));

        var back = await Query(client, WithToken(Body, previous.Extensions["page_token"]));
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal("Bob", await FirstName(back));
    }

    [Fact]
    public async Task Windowed_First_Page_Omits_Prev_And_Final_Page_Replays_Prev() {
        var client = _factory.CreateClient();

        var first = await Query(client, Windowed);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Cleo", await FirstName(first));

        var links = Links(first);
        Assert.DoesNotContain(links, link => link.Rel == "prev");
        var next = Assert.Single(links, link => link.Rel == "next");
        Assert.Equal("/v1/insight:query", Path(next.Target));
        Assert.NotNull(next.Extensions);
        Assert.True(next.Extensions!.ContainsKey("page_token"));

        var second = await Query(client, WithToken(Windowed, next.Extensions!["page_token"]));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Ada", await FirstName(second));
        var secondLinks = Links(second);
        Assert.DoesNotContain(secondLinks, link => link.Rel == "next");
        var previous = Assert.Single(secondLinks, link => link.Rel == "prev");
        Assert.Equal("/v1/insight:query", Path(previous.Target));
        Assert.NotNull(previous.Extensions);
        Assert.True(previous.Extensions!.ContainsKey("page_token"));

        var back = await Query(client, WithToken(Windowed, previous.Extensions["page_token"]));
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal("Cleo", await FirstName(back));
    }

    [Fact]
    public async Task Exhausted_Query_Emits_Self_Only() {
        var client = _factory.CreateClient();

        var response = await Query(client, Body.Replace("\"page_size\": 1", "\"page_size\": 10"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var links = Links(response);
        Assert.Single(links, link => link.Rel == "self");
        Assert.DoesNotContain(links, link => link.Rel is "next" or "prev");
    }

    private static Task<HttpResponseMessage> Query(HttpClient client, string body) {
        return client.PostAsync("/v1/insight:query", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static string WithToken(string body, string token) =>
        body[..^1] + ",\"page_token\":" + JsonSerializer.Serialize(token) + "}";

    private static async Task<string?> FirstName(HttpResponseMessage response) {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("rows")[0].GetProperty("full_name").GetString();
    }

    private static IReadOnlyList<WebLink> Links(HttpResponseMessage response) {
        var values = response.Headers.GetValues(LinkHeader.Name).ToList();
        Assert.Single(values);
        var links = LinkHeader.Parse(values[0], response.RequestMessage!.RequestUri);
        Assert.Equal(links.Count, links.Select(Key).Distinct().Count());
        return links;
    }

    private static string Key(WebLink link) => string.Join("|",
        link.Target, link.Rel, link.Anchor, link.Hreflang, link.Media, link.Title, link.TitleStar, link.Type,
        link.Extensions is null
            ? string.Empty
            : string.Join(";", link.Extensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                              .Select(pair => pair.Key + "=" + pair.Value)));

    private static string Path(string target) => new Uri(target, UriKind.RelativeOrAbsolute).AbsolutePath;
}
