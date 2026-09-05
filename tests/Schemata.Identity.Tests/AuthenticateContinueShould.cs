using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Foundation.Runtime;
using Schemata.Identity.Skeleton.Entities;
using Xunit;

namespace Schemata.Identity.Tests;

public class AuthenticateContinueShould
{
    [Fact]
    public async Task Resume_The_Request_That_Triggered_The_SignIn() {
        var context = LoginContinuationShould.Context("https://id.example.com/sign-in");
        await LoginContinuation.RedirectToLoginAsync(Redirect(context));

        var result = Continue(context, LoginContinuationShould.Issued(context));

        Assert.Equal("/console/orders?page=2", Assert.IsType<RedirectResult>(result).Url);
    }

    [Fact]
    public void Reject_A_Continuation_Pointing_Off_Site() {
        var context = LoginContinuationShould.Context("https://id.example.com/sign-in");
        var forged = LoginContinuation.Protector(context.RequestServices).Protect("https://evil.example.com/steal");

        Assert.Throws<ValidationException>(() => Continue(context, forged));
    }

    [Fact]
    public void Reject_A_Continuation_The_Server_Did_Not_Issue() {
        var context = LoginContinuationShould.Context("https://id.example.com/sign-in");

        Assert.Throws<ValidationException>(() => Continue(context, "not-a-protected-payload"));
    }

    [Fact]
    public void Reject_A_Continuation_Issued_For_Another_Purpose() {
        var context = LoginContinuationShould.Context("https://id.example.com/sign-in");
        var foreign = context.RequestServices.GetRequiredService<IDataProtectionProvider>()
                             .CreateProtector("Some.Other.Purpose")
                             .Protect("/console/orders");

        Assert.Throws<ValidationException>(() => Continue(context, foreign));
    }

    [Fact]
    public void Reject_A_Missing_Continuation() {
        var context = LoginContinuationShould.Context("https://id.example.com/sign-in");

        Assert.Throws<ValidationException>(() => Continue(context, null));
    }

    private static IActionResult Continue(HttpContext context, string? token) {
        var action     = new ActionContext(context, new(), new ControllerActionDescriptor());
        var controller = new AuthenticateController<SchemataUser>(null!, null!) {
            ControllerContext = new(action),
            Url               = new UrlHelper(action),
        };

        return controller.Continue(token, context.RequestServices.GetRequiredService<IDataProtectionProvider>());
    }

    private static RedirectContext<CookieAuthenticationOptions> Redirect(HttpContext context) {
        return new(context, new("Identity.Application", "Identity.Application", typeof(CookieAuthenticationHandler)),
                   new(), new(), "/ignored");
    }
}
