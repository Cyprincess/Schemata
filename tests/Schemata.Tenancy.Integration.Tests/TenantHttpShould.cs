using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Middlewares;
using Schemata.Tenancy.Foundation.Resolvers;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantHttpShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_Scheme_Binds_Principal_Tenant_And_Rejects_Request_Conflict(bool mvcFilter) {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observations = new Observations();
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services => {
            services.AddRouting();
            services.AddHttpContextAccessor();
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TenantAuthentication>("tenant", null);
            services.AddAuthorization();
            services.AddControllers(options => {
                if (mvcFilter) options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder("tenant").RequireAuthenticatedUser().Build()));
            }).AddApplicationPart(typeof(TenantProbeController).Assembly);
            services.AddSingleton(observations);
            services.AddDbContextFactory<TenantStorageShould.Database>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            services.AddRepository<SchemataTenant, EfCoreRepository<TenantStorageShould.Database, SchemataTenant>>();
            services.AddRepository<SchemataTenantHost, EfCoreRepository<TenantStorageShould.Database, SchemataTenantHost>>();
            services.AddScoped<ITenantResolver, RequestHeaderResolver>();
            services.AddScoped<ITenantResolver, RequestPrincipalResolver>();
            new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
                .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
            services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, tenant, _) => tenant.AddScoped<TenantProbe>()));
        }).Configure(app => {
            app.UseRouting();
            app.UseSchemataExceptionHandler();
            app.UseMiddleware<SchemataTenancyMiddleware<SchemataTenant>>();
            app.UseAuthentication();
            app.UseMiddleware<SchemataTenantPrincipalMiddleware<SchemataTenant>>();
            app.UseAuthorization();
            app.UseMiddleware<TenantExecutionMiddleware>();
            app.UseEndpoints(endpoints => {
                if (mvcFilter) endpoints.MapControllers();
                else endpoints.MapGet("/probe", async context => {
                    var probe = context.RequestServices.GetRequiredService<TenantProbe>();
                    await context.Response.WriteAsync($"{probe.Uid:D}|{TenantContext.Current.Uid:D}");
                }).RequireAuthorization(new AuthorizationPolicyBuilder("tenant").RequireAuthenticatedUser().Build());
            });
        })).StartAsync();
        var a = new SchemataTenant { Name = "a" };
        var b = new SchemataTenant { Name = "b" };
        await using (var scope = host.Services.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<TenantStorageShould.Database>().Database.EnsureCreatedAsync();
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await manager.CreateAsync(a, default);
            await manager.CreateAsync(b, default);
        }
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("x-auth-tenant", a.Uid.ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"{a.Uid:D}|{a.Uid:D}", await response.Content.ReadAsStringAsync());
        if (mvcFilter) Assert.Equal(a.Uid, observations.DisposedUnder);
        using var conflict = new HttpRequestMessage(HttpMethod.Get, "/probe");
        conflict.Headers.Add("x-tenant-id", a.Uid.ToString());
        conflict.Headers.Add("x-auth-tenant", b.Uid.ToString());
        using var rejected = await client.SendAsync(conflict);
        Assert.NotEqual(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("error", await rejected.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        await host.StopAsync();
    }

    public sealed class TenantProbe(SchemataTenant tenant) { public Guid Uid => tenant.Uid; }
    public sealed class Observations { public Guid? DisposedUnder; }

    public sealed class TenantAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (!Request.Headers.TryGetValue("x-auth-tenant", out var uid)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("Tenant", uid.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

[Route("/probe")]
public sealed class TenantProbeController(TenantHttpShould.Observations observations) : ControllerBase, IDisposable
{
    [HttpGet]
    public string Get([FromServices] TenantHttpShould.TenantProbe probe) => $"{probe.Uid:D}|{TenantContext.Current.Uid:D}";
    public void Dispose() => observations.DisposedUnder = TenantContext.Current.Uid;
}
