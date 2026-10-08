using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Schemata.Core.Filters;
using Xunit;

namespace Schemata.Authorization.Tests;

public class SecurityHeadersShould
{
    [Fact]
    public void Apply_Frame_Guard_And_Cache_Headers_To_Every_Result() {
        var http    = new DefaultHttpContext();
        var context = new ResultExecutingContext(
            new(http, new(), new()),
            [], new EmptyResult(), new());

        new NoCacheResponseAttribute().OnResultExecuting(context);

        Assert.Equal("no-store", http.Response.Headers.CacheControl);
        Assert.Equal("no-cache", http.Response.Headers.Pragma);
        Assert.Equal("DENY",     http.Response.Headers.XFrameOptions);
        Assert.Equal("frame-ancestors 'self'", http.Response.Headers.ContentSecurityPolicy);
    }
}