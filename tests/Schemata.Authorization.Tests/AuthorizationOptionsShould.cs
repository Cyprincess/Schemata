using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

[Trait("Category", "Integration")]
public class AuthorizationOptionsShould
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Authenticate_And_Challenge_Profile_Using_Once_Configured_Final_Schemes(bool optionsFirst) {
        var calls = 0;
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(TestSecurityKeys.CreateTokenService(new() { Issuer = "https://as.example" }));
        builder.Services.AddSingleton(Mock.Of<ITokenStore<SchemataToken>>());
        builder.Services.AddSingleton(Mock.Of<IAuthorizationSignInService>());
        builder.Services.AddSingleton(Mock.Of<IAuthorizationSignInHttpWriter>());
        builder.UseSchemata(schema => schema.UseAuthorization(o => {
            calls++;
            o.Issuer = "https://as.example";
            o.BearerScheme = "initial-bearer";
            o.CodeScheme = "initial-code";
        }).UseUserInfo());
        builder.Services.PostConfigure<SchemataAuthorizationOptions>(o => {
            o.BearerScheme = "final-bearer";
            o.CodeScheme = "final-code";
        });
        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (optionsFirst) _ = services.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value;
        var policy = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(SchemataAuthorizationPolicies.Profile);
        Assert.NotNull(policy);
        var context = new DefaultHttpContext { RequestServices = services };
        var result = await services.GetRequiredService<IPolicyEvaluator>().AuthenticateAsync(policy, context);
        Assert.False(result.Succeeded);
        foreach (var scheme in policy.AuthenticationSchemes) await context.ChallengeAsync(scheme);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(new[] { "final-bearer" }, policy.AuthenticationSchemes);
        var schemes = services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.NotNull(await schemes.GetSchemeAsync("final-code"));
        Assert.Null(await schemes.GetSchemeAsync("initial-code"));
        Assert.Null(await schemes.GetSchemeAsync("initial-bearer"));
        Assert.Equal("final-bearer", services.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value.BearerScheme);
        Assert.Equal(1, calls);
    }
}
