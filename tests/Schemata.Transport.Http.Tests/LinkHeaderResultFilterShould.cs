using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Linking;
using Schemata.Transport.Http.Linking;
using Schemata.Core.Linking;
using Xunit;
using Schemata.Core.Building;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;


namespace Schemata.Transport.Http.Tests;

public class LinkHeaderResultFilterShould
{
    [Fact]
    public void Challenge_Does_Not_Write_A_Link_Header() {
        var response = Execute(new ChallengeResult(), new WebLink("/v1/items", "self"));

        Assert.False(response.Response.Headers.ContainsKey(LinkHeader.Name));
    }

    [Fact]
    public void Forbid_Does_Not_Write_A_Link_Header() {
        var response = Execute(new ForbidResult(), new WebLink("/v1/items", "self"));

        Assert.False(response.Response.Headers.ContainsKey(LinkHeader.Name));
    }

    [Fact]
    public void Unregistered_Target_Drops_The_Link() {
        var response = Execute(new OkObjectResult(new object()), new WebLink("not a URI", "self"));

        Assert.False(response.Response.Headers.ContainsKey(LinkHeader.Name));
    }

    [Fact]
    public void Valid_Target_Writes_One_Header() {
        var response = Execute(new OkObjectResult(new object()), new WebLink("/v1/items", "next"));

        Assert.Equal("</v1/items>; rel=\"next\"", response.Response.Headers[LinkHeader.Name].ToString());
    }

    [Theory]
    [InlineData("bad\nlabel")]
    [InlineData("bad\0label")]
    [InlineData("bad\u007flabel")]
    public void Invalid_Ancillary_Title_Omits_Only_That_Link_And_Preserves_Success(string title) {
        var payload = new { Value = "completed" };
        var result = new OkObjectResult(payload);
        var response = Execute(result, new WebLink("/v1/bad", "related", Title: title), new WebLink("/v1/items", "self"));
        Assert.Equal(200, result.StatusCode);
        Assert.Same(payload, result.Value);
        Assert.Equal("</v1/items>; rel=\"self\"", response.Response.Headers[LinkHeader.Name].ToString());
    }

    [Fact]
    public void Invalid_Ancillary_Input_Omits_Only_That_Link_And_Preserves_Success() {
        var payload = new { Value = "completed" };
        var result = new OkObjectResult(payload);
        var response = Execute(result, new WebLink("/v1/bad", "next", Extensions: new Dictionary<string, string> {
            ["page_token"] = "consumer\ninput",
        }), new WebLink("/v1/items", "self"));
        Assert.Equal(200, result.StatusCode);
        Assert.Same(payload, result.Value);
        Assert.Equal("</v1/items>; rel=\"self\"", response.Response.Headers[LinkHeader.Name].ToString());
    }

    [Theory]
    [InlineData("unregistered")]
    [InlineData("disabled")]
    [InlineData("excluded")]
    [InlineData("custom")]
    public void Resource_Target_Requires_A_Published_Operation(string reason) {
        var registry = new ResourceRegistry();
        if (reason != "unregistered") registry.Add(new ResourceAttribute<Entry> {
            Endpoints = reason == "disabled" ? [GrpcResourceAttribute.Name] : [HttpResourceAttribute.Name],
            Operations = reason == "excluded" ? [Operations.List] : [Operations.Get],
        }, []);
        var projector = new HttpLinkProjector([registry], []);
        var target = new ResourceInstanceTarget(typeof(Entry), "entries/one", reason == "custom" ? "catch" : null);
        var context = new LinkContext(target, null, null);
        Assert.Null(projector.Project(new("related", target), context, new DefaultHttpContext().Request));
    }

    [CanonicalName("entries/{entry}")]
    private sealed class Entry;

    private static DefaultHttpContext Execute(IActionResult result, params WebLink[] links) {
        var http = new DefaultHttpContext();
        using var services = new ServiceCollection()
                             .AddKeyedSingleton<ILinkContributor>(LinkTransports.Http, new StaticLinks(links))
                             .AddSingleton<HttpLinkProjector>()
                             .AddSingleton<ILinkRouteProvider, TestRoutes>()
                             .BuildServiceProvider();
        http.RequestServices = services;

        var context = new ResultExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            Array.Empty<IFilterMetadata>(),
            result,
            new object());

        new LinkHeaderResultFilter().OnResultExecuting(context);

        return http;
    }

    private sealed class StaticLinks(params WebLink[] links) : ILinkContributor
    {
        public IEnumerable<SemanticLink> GetLinks(LinkContext _) {
            foreach (var link in links) {
                IReadOnlyDictionary<string, string?>? parameters = null;
                if (link.Extensions is { } extensions) {
                    var values = new Dictionary<string, string?>();
                    foreach (var pair in extensions) values.Add(pair.Key, pair.Value);
                    parameters = values;
                }
                yield return new(link.Rel, new DomainOperationTarget(link.Target, parameters), link.Title);
            }
        }
    }

    private sealed class TestRoutes : ILinkRouteProvider
    {
        public string? GetRoute(string operation) => operation.StartsWith("/", StringComparison.Ordinal) ? operation : null;
        public string? GetOperation(HttpRequest request) => null;
    }
}
