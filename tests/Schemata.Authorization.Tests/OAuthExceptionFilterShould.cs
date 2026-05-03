using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Filters;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class OAuthExceptionFilterShould
{
    private static ExceptionContext CreateContext(OAuthException exception) {
        var httpContext  = new DefaultHttpContext();
        var routeData    = new RouteData();
        var actionCtx    = new ActionContext(httpContext, routeData, new());
        var exceptionCtx = new ExceptionContext(actionCtx, new List<IFilterMetadata>()) { Exception = exception };
        return exceptionCtx;
    }

    private static OAuthExceptionFilter CreateFilter(string? issuer) {
        var options = Options.Create(new SchemataAuthorizationOptions { Issuer = issuer });
        return new(options);
    }

    [Fact]
    public void AddsIssToQueryRedirect_WhenIssuerConfigured() {
        var filter = CreateFilter("https://auth.example.com");
        var exception = new OAuthException(OAuthErrors.InvalidScope, "scope denied") {
            RedirectUri = "https://client.example.com/callback", State = "xyz", ResponseMode = ResponseModes.Query,
        };
        var ctx = CreateContext(exception);

        filter.OnException(ctx);

        var redirect = Assert.IsType<RedirectResult>(ctx.Result);
        Assert.Contains("iss=https%3A%2F%2Fauth.example.com", redirect.Url);
        Assert.Contains("error=invalid_scope", redirect.Url);
        Assert.Contains("state=xyz", redirect.Url);
        Assert.True(ctx.ExceptionHandled);
    }

    [Fact]
    public void AddsIssToFragmentRedirect_WhenIssuerConfigured() {
        var filter = CreateFilter("https://auth.example.com");
        var exception = new OAuthException(OAuthErrors.AccessDenied, "denied") {
            RedirectUri = "https://client.example.com/callback", ResponseMode = ResponseModes.Fragment,
        };
        var ctx = CreateContext(exception);

        filter.OnException(ctx);

        var redirect = Assert.IsType<RedirectResult>(ctx.Result);
        Assert.Contains("#", redirect.Url);
        Assert.Contains("iss=https%3A%2F%2Fauth.example.com", redirect.Url);
    }

    [Fact]
    public void OmitsIss_WhenIssuerNotConfigured() {
        var filter = CreateFilter(null);
        var exception = new OAuthException(OAuthErrors.InvalidScope, "scope denied") {
            RedirectUri = "https://client.example.com/callback", ResponseMode = ResponseModes.Query,
        };
        var ctx = CreateContext(exception);

        filter.OnException(ctx);

        var redirect = Assert.IsType<RedirectResult>(ctx.Result);
        Assert.DoesNotContain("iss=", redirect.Url);
    }

    [Fact]
    public void KeepsJsonResponse_WhenNoRedirectUri() {
        var filter    = CreateFilter("https://auth.example.com");
        var exception = new OAuthException(OAuthErrors.InvalidClient, "bad client");
        var ctx       = CreateContext(exception);

        filter.OnException(ctx);

        var json = Assert.IsType<JsonResult>(ctx.Result);
        Assert.Equal(exception.Status, json.StatusCode);
    }

    [Fact]
    public void IgnoresNonOAuthException() {
        var filter = CreateFilter("https://auth.example.com");
        var ctx    = CreateContext(null!);
        ctx.Exception = new InvalidOperationException("boom");

        filter.OnException(ctx);

        Assert.Null(ctx.Result);
        Assert.False(ctx.ExceptionHandled);
    }
}
