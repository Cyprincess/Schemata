using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Foundation.Runtime;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
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

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Installed_Tenancy_Resumes_Issuing_Scope_And_Rejects_Other_Scopes(bool host) {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        using var root = TenantedRoot();
        var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        using var cache = new MemoryCacheTenantProviderCache(tenancy);
        var scopes = new SchemataTenantServiceScopeFactory<SchemataTenant>(
            root, new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, tenancy));
        await using var issuing = await scopes.CreateAsync(new(host ? (Guid?)null : first));
        var context = new DefaultHttpContext {
            RequestServices = issuing.ServiceProvider,
            Request = { Path = "/console/orders", QueryString = new("?page=2") },
        };
        await LoginContinuation.RedirectToLoginAsync(Redirect(context));
        var token = LoginContinuationShould.Issued(context);
        Assert.Equal("/console/orders?page=2", Assert.IsType<RedirectResult>(Continue(context, token)).Url);
        await using (var other = await scopes.CreateAsync(new(host ? first : (Guid?)null))) {
            context.RequestServices = other.ServiceProvider;
            Assert.Throws<ValidationException>(() => Continue(context, token));
        }
        await using (var sibling = await scopes.CreateAsync(new(second))) {
            context.RequestServices = sibling.ServiceProvider;
            Assert.Throws<ValidationException>(() => Continue(context, token));
        }
        context.RequestServices = issuing.ServiceProvider;
        Assert.Equal("/console/orders?page=2", Assert.IsType<RedirectResult>(Continue(context, token)).Url);
    }

    private static ServiceProvider TenantedRoot() {
        var tenants = new ConcurrentDictionary<Guid, SchemataTenant>();
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                tenants.GetOrAdd(identifier, static value => new SchemataTenant { Uid = value, Timestamp = Guid.NewGuid(), Name = value.ToString() })));
        var services = new ServiceCollection()
            .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
            .AddSingleton(manager.Object)
            .Configure<SchemataIdentityOptions>(options => options.LoginUri = "/sign-in");
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => state.CookiePrefix = "c.");
        return services.BuildServiceProvider();
    }

    private static IActionResult Continue(HttpContext context, string? token) {
        var action     = new ActionContext(context, new(), new ControllerActionDescriptor());
        var controller = new AuthenticateController<SchemataUser>(null!, null!, Options.Create(new SchemataIdentityOptions()), []) {
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
