using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class ResponseModeServiceShould
{
    private static Dictionary<string, string?> Params(params (string key, string? value)[] pairs) {
        var dict                              = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs) dict[k] = v;
        return dict;
    }

    [Fact]
    public void CreateCallback_Query_AppendsQueryString() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb",
                                                        Params(("code", "abc123"), ("state", "xyz")), "query");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://client.example.com/cb?code=abc123&state=xyz", redirect.Url);
    }

    [Fact]
    public void CreateCallback_Query_UsesSeparatorWhenQueryAlreadyPresent() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb?existing=1",
                                                        Params(("code", "abc123")), "query");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://client.example.com/cb?existing=1&code=abc123", redirect.Url);
    }

    [Fact]
    public void CreateCallback_Fragment_AppendsFragment() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb",
                                                        Params(("access_token", "tok"), ("token_type", "Bearer")),
                                                        "fragment");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://client.example.com/cb#access_token=tok&token_type=Bearer", redirect.Url);
    }

    [Fact]
    public void CreateCallback_FormPost_ReturnsHtmlWithHiddenFields() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb",
                                                        Params(("code", "abc123"), ("state", "xyz")), "form_post");

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("text/html", content.ContentType);
        Assert.Equal(200, content.StatusCode);
        Assert.Contains("action=\"https://client.example.com/cb\"", content.Content);
        Assert.Contains("name=\"code\"", content.Content);
        Assert.Contains("value=\"abc123\"", content.Content);
        Assert.Contains("name=\"state\"", content.Content);
        Assert.Contains("document.forms[0].submit()", content.Content);
    }

    [Fact]
    public void CreateCallback_FormPost_HtmlEncodesRedirectUri() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb?a=1&b=2",
                                                        Params(("code", "abc")), "form_post");

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("action=\"https://client.example.com/cb?a=1&amp;b=2\"", content.Content);
    }

    [Fact]
    public void CreateCallback_FormPost_HtmlEncodesParameterValues() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb",
                                                        Params(("state", "<script>alert(1)</script>")), "form_post");

        var content = Assert.IsType<ContentResult>(result);
        Assert.DoesNotContain("<script>", content.Content);
        Assert.Contains("&lt;script&gt;", content.Content);
    }

    [Fact]
    public void CreateCallback_NullValues_ExcludedFromOutput() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb",
                                                        Params(("code", "abc"), ("state", null)), "query");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://client.example.com/cb?code=abc", redirect.Url);
    }

    [Fact]
    public void CreateCallback_UnknownMode_FallsBackToQuery() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb", Params(("code", "abc")),
                                                        "unknown_mode");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.StartsWith("https://client.example.com/cb?", redirect.Url);
    }

    [Theory]
    [InlineData("query", "code", "query")]
    [InlineData("fragment", "code", "fragment")]
    [InlineData("form_post", "token", "form_post")]
    public void ResolveMode_ReturnsExplicitModeWhenProvided(string mode, string responseType, string expected) {
        Assert.Equal(expected, ResponseModeService.ResolveMode(mode, responseType));
    }

    [Theory]
    [InlineData("code", ResponseModes.Query)]
    [InlineData("code id_token", ResponseModes.Fragment)]
    [InlineData("token", ResponseModes.Fragment)]
    [InlineData("id_token", ResponseModes.Fragment)]
    [InlineData("code token", ResponseModes.Fragment)]
    public void ResolveMode_DefaultsBasedOnResponseType(string responseType, string expected) {
        Assert.Equal(expected, ResponseModeService.ResolveMode(null, responseType));
    }

    [Fact]
    public void ResolveMode_TreatsEmptyModeAsUnspecified() {
        Assert.Equal(ResponseModes.Query, ResponseModeService.ResolveMode("", "code"));
        Assert.Equal(ResponseModes.Fragment, ResponseModeService.ResolveMode("", "token"));
    }

    [Fact]
    public void CreateCallback_Query_UrlEncodesSpecialChars() {
        var result = ResponseModeService.CreateCallback("https://client.example.com/cb",
                                                        Params(("state", "hello world+test")), "query");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Contains("state=hello%20world%2Btest", redirect.Url);
    }
}
