using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Runtime;
using Xunit;

namespace Schemata.Identity.Tests;

public class LoginContinuationShould
{
    private const string Target = "/console/orders?page=2";

    [Fact]
    public async Task Answer401_WhenNoLoginUriIsConfigured() {
        var context = Context(null);

        await LoginContinuation.RedirectToLoginAsync(Redirect(context));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task RedirectToTheLoginPage_WithAnOpaqueContinuation() {
        var context = Context("https://id.example.com/sign-in");

        await LoginContinuation.RedirectToLoginAsync(Redirect(context));

        var location = context.Response.Headers.Location.ToString();
        Assert.StartsWith("https://id.example.com/sign-in?", location);

        var payload = HttpUtility.ParseQueryString(location[location.IndexOf('?')..])[LoginContinuation.Parameter];
        Assert.False(string.IsNullOrWhiteSpace(payload));
        Assert.DoesNotContain("console", payload);
        Assert.DoesNotContain("orders", payload);
    }

    internal static string Issued(HttpContext context) {
        var location = context.Response.Headers.Location.ToString();
        var payload  = HttpUtility.ParseQueryString(location[location.IndexOf('?')..])[LoginContinuation.Parameter];

        return payload!;
    }

    internal static DefaultHttpContext Context(string? loginUri) {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.Configure<SchemataIdentityOptions>(o => o.LoginUri = loginUri);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path        = "/console/orders";
        context.Request.QueryString = new("?page=2");

        return context;
    }

    private static RedirectContext<CookieAuthenticationOptions> Redirect(HttpContext context) {
        return new(context, new("Identity.Application", "Identity.Application", typeof(CookieAuthenticationHandler)),
                   new(), new(), "/ignored");
    }
}
