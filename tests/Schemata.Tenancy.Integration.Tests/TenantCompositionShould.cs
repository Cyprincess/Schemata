using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Middlewares;
using Schemata.Tenancy.Foundation.Resolvers;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantCompositionShould
{
    [Fact]
    public void Publish_The_Framework_Implementations_Under_The_Public_And_Keyed_Registrations() {
        using var provider = Compose(withTenancy: false).BuildServiceProvider();

        Assert.IsType<PolicyEvaluator>(provider.GetRequiredService<IPolicyEvaluator>());
        Assert.IsType<PolicyEvaluator>(
            provider.GetRequiredKeyedService<IPolicyEvaluator>(SchemataAuthenticationFeature.PolicyEvaluatorServiceKey));
        var factory = provider.GetRequiredKeyedService<IControllerFactory>(SchemataControllersFeature.ControllerFactoryServiceKey);
        Assert.Same(factory, provider.GetRequiredService<IControllerFactory>());
    }

    [Fact]
    public async Task Compose_The_Tenant_Evaluator_And_Controller_Factory_Over_The_Framework_Implementations() {
        using var provider = Compose(withTenancy: true).BuildServiceProvider();

        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();
        Assert.IsNotType<PolicyEvaluator>(evaluator);
        Assert.IsType<PolicyEvaluator>(
            provider.GetRequiredKeyedService<IPolicyEvaluator>(SchemataAuthenticationFeature.PolicyEvaluatorServiceKey));
        Assert.NotSame(
            provider.GetRequiredKeyedService<IControllerFactory>(SchemataControllersFeature.ControllerFactoryServiceKey),
            provider.GetRequiredService<IControllerFactory>());

        var context = new DefaultHttpContext { RequestServices = provider };
        var result = await evaluator.AuthorizeAsync(
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            AuthenticateResult.NoResult(),
            context,
            null);

        Assert.True(result.Challenged);
    }

    [Fact]
    public void Compose_The_Controller_Factory_Once_When_The_Seam_Is_Registered_Twice() {
        var services = Compose(withTenancy: false);
        services.AddSchemataControllerFactory();

        using var provider = services.BuildServiceProvider();
        var       keyed    = provider.GetRequiredKeyedService<IControllerFactory>(SchemataControllersFeature.ControllerFactoryServiceKey);

        Assert.Same(keyed, provider.GetRequiredService<IControllerFactory>());
    }

    [Fact]
    public void Ignore_A_Keyed_Controller_Factory_When_Selecting_The_Framework_Registration() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        services.AddKeyedSingleton<IControllerFactory>("custom", (_, _) => throw new NotSupportedException());
        services.AddSchemataControllerFactory();

        using var provider = services.BuildServiceProvider();
        var       keyed    = provider.GetRequiredKeyedService<IControllerFactory>(SchemataControllersFeature.ControllerFactoryServiceKey);

        Assert.Same(keyed, provider.GetRequiredService<IControllerFactory>());
    }

    [Fact]
    public async Task Compose_The_Tenant_Evaluator_When_Authorization_Is_Registered_First() {
        var tenant  = new SchemataTenant { Uid = Guid.NewGuid(), Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(tenant.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services => {
            services.AddRouting();
            services.AddHttpContextAccessor();
            services.AddSingleton(manager.Object);
            services.AddAuthentication("tenant").AddScheme<AuthenticationSchemeOptions, PrincipalScheme>("tenant", null);
            services.AddAuthorization();
            services.AddSchemataPolicyEvaluator();
            services.AddScoped<ITenantResolver, RequestPrincipalResolver>();
            new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
                .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        }).Configure(app => {
            app.UseRouting();
            app.UseMiddleware<SchemataTenancyMiddleware<SchemataTenant>>();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<TenantExecutionMiddleware>();
            app.UseEndpoints(endpoints => {
                endpoints.MapGet("/probe", async context => {
                    var resolved = context.RequestServices.GetRequiredService<SchemataTenant>();
                    await context.Response.WriteAsync($"{resolved.Uid:D}|{TenantContext.Current.Uid:D}");
                }).RequireAuthorization(new AuthorizationPolicyBuilder("tenant").RequireAuthenticatedUser().Build());
            });
        })).StartAsync();

        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("x-tenant-claim", tenant.Uid.ToString("D"));
        using var response = await client.GetAsync("/probe");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"{tenant.Uid:D}|{tenant.Uid:D}", await response.Content.ReadAsStringAsync());
        await host.StopAsync();
    }

    private static IServiceCollection Compose(bool withTenancy) {
        var configuration = new ConfigurationBuilder().Build();
        var services      = new ServiceCollection();
        services.AddLogging();
        new SchemataAuthenticationFeature().ConfigureServices(services, new SchemataOptions(), new Configurators(), configuration, null!);
        new SchemataControllersFeature().ConfigureServices(services, new SchemataOptions(), new Configurators(), configuration, null!);
        if (withTenancy) {
            new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
                .ConfigureServices(services, new SchemataOptions(), new Configurators(), configuration, null!);
        }

        return services;
    }

    private sealed class PrincipalScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (!Request.Headers.TryGetValue("x-tenant-claim", out var uid)) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("Tenant", uid.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
