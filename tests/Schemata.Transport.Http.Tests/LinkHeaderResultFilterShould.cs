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
using Xunit;

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
    public void Invalid_Target_Drops_The_Header_Without_Failing() {
        var response = Execute(new OkObjectResult(new object()), new WebLink("not a URI", "self"));

        Assert.False(response.Response.Headers.ContainsKey(LinkHeader.Name));
    }

    [Fact]
    public void Valid_Target_Writes_One_Header() {
        var response = Execute(new OkObjectResult(new object()), new WebLink("/v1/items?page=2", "next"));

        Assert.Equal("</v1/items?page=2>; rel=\"next\"",
                     response.Response.Headers[LinkHeader.Name].ToString());
    }

    private static DefaultHttpContext Execute(IActionResult result, params WebLink[] links) {
        var http = new DefaultHttpContext();
        using var services = new ServiceCollection()
                             .AddSingleton<ILinkContributor>(new StaticLinks(links))
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
        public IEnumerable<WebLink> GetLinks(LinkContext _) => links;
    }
}
