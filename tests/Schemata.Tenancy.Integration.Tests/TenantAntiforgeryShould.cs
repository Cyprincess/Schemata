using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Core.State;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Middlewares;
using Schemata.Tenancy.Foundation.Resolvers;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Foundation.State;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class TenantAntiforgeryShould
{
    private const string HeaderName = "RequestVerificationToken";

    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Token_Pair_Issued_By_A_Profile_Only_Validates_Within_Its_Own_Branch() {
        using var host    = await CreateServerAsync(StandardState());
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        var (tokenA, cookiesA)           = await EstablishAsync(client, "A");
        var (tokenB, cookiesB)           = await EstablishAsync(client, "B");
        var (tokenGlobal, cookiesGlobal) = await EstablishAsync(client, "global");

        Assert.Single(cookiesA, value => value.StartsWith(".a" + AntiforgeryOptions.DefaultCookiePrefix, StringComparison.Ordinal));
        Assert.DoesNotContain(cookiesA, value => value.StartsWith(AntiforgeryOptions.DefaultCookiePrefix, StringComparison.Ordinal));
        Assert.Single(cookiesB, value => value.StartsWith(".b" + AntiforgeryOptions.DefaultCookiePrefix, StringComparison.Ordinal));
        Assert.Single(cookiesGlobal, value => value.StartsWith(AntiforgeryOptions.DefaultCookiePrefix, StringComparison.Ordinal));
        Assert.DoesNotContain(cookiesGlobal, value => value.StartsWith(".a" + AntiforgeryOptions.DefaultCookiePrefix, StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.OK, await SubmitAsync(client, "A", "metadata", tokenA));
        Assert.Equal(HttpStatusCode.OK, await SubmitAsync(client, "A", "direct", tokenA));
        Assert.Equal(HttpStatusCode.OK, await SubmitAsync(client, "B", "metadata", tokenB));
        Assert.Equal(HttpStatusCode.OK, await SubmitAsync(client, "global", "metadata", tokenGlobal));

        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "B", "metadata", tokenA));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "B", "direct", tokenA));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "A", "metadata", tokenB));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "global", "metadata", tokenA));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "A", "metadata", tokenGlobal));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "global", "direct", tokenA));
    }

    [Fact]
    public async Task Protected_Posts_Without_Or_With_Broken_Tokens_Are_Rejected() {
        using var host    = await CreateServerAsync(StandardState());
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        var (tokenA, _) = await EstablishAsync(client, "A");

        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "A", "metadata", null));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "A", "direct", null));

        using var fresh = CreateClient(Client(host));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(fresh, "A", "metadata", tokenA));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(fresh, "A", "direct", tokenA));

        handler.Jar.Tamper(".a" + AntiforgeryOptions.DefaultCookiePrefix, "tampered");
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "A", "metadata", tokenA));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "A", "direct", tokenA));
    }

    [Fact]
    public async Task Application_Additional_Data_Provider_Fails_Validation_Under_Its_Own_Tenant() {
        var additional = new Mock<IAntiforgeryAdditionalDataProvider>();
        additional.Setup(value => value.GetAdditionalData(It.IsAny<HttpContext>())).Returns("tenant-additional");
        additional.Setup(value => value.ValidateAdditionalData(It.IsAny<HttpContext>(), It.IsAny<string>())).Returns(false);

        using var host = await CreateServerAsync(state => {
            state.CookiePrefix = state.TenantUid == TenantA ? ".a" : ".b";
            state.Services.AddSingleton(additional.Object);
            state.UseAntiforgery(_ => { });
        });
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        var (tokenA, _) = await EstablishAsync(client, "A");
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "A", "metadata", tokenA));
        Assert.Equal(HttpStatusCode.BadRequest, await SubmitAsync(client, "A", "direct", tokenA));

        var (tokenGlobal, _) = await EstablishAsync(client, "global");
        Assert.Equal(HttpStatusCode.OK, await SubmitAsync(client, "global", "metadata", tokenGlobal));

        additional.Verify(
            value => value.ValidateAdditionalData(It.IsAny<HttpContext>(), "tenant-additional"),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Tenant_Branch_Without_Global_Antiforgery_Registration_Fails_Loudly() {
        using var host    = await CreateServerAsync(StandardState(), globalFeature: false);
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/A/af/token"));
        Assert.Contains("antiforgery", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tenant_Graph_Resolves_Native_Antiforgery_Services_Outside_The_Host_Instance() {
        var tenant  = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(manager.Object);
        services.AddDataProtection();
        services.AddAntiforgery();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => {
            state.CookiePrefix = "tenant-a";
            state.UseAntiforgery(_ => { });
        });
        await using var root     = services.BuildServiceProvider();
        var             tenancy  = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var             providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, new MemoryCacheTenantProviderCache(tenancy), tenancy);

        await using var scope       = await new SchemataTenantServiceScopeFactory<SchemataTenant>(root, providers).CreateAsync(new(tenant.Uid));
        var             antiforgery = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var             cookieName  = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<AntiforgeryOptions>>().CurrentValue.Cookie.Name;

        Assert.NotSame(root.GetRequiredService<IAntiforgery>(), antiforgery);
        Assert.StartsWith("tenant-a" + AntiforgeryOptions.DefaultCookiePrefix, cookieName, StringComparison.Ordinal);
        Assert.StartsWith(AntiforgeryOptions.DefaultCookiePrefix, root.GetRequiredService<IOptionsMonitor<AntiforgeryOptions>>().CurrentValue.Cookie.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseAntiforgery_Without_Cookie_Prefix_Fails_At_Tenant_Container_Build() {
        var tenant  = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.AddDataProtection();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => state.UseAntiforgery(_ => { }));
        await using var root    = services.BuildServiceProvider();
        var             tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, new MemoryCacheTenantProviderCache(tenancy), tenancy);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await providers.CreateServiceProviderAsync(TenantA));
        Assert.Contains("cookie prefix", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_Antiforgery_Declaration_Fails_At_Tenant_Container_Build() {
        var tenant  = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.AddDataProtection();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state => {
            state.CookiePrefix = "tenant-a";
            state.UseAntiforgery(_ => { });
            state.UseAntiforgery(_ => { });
        });
        await using var root    = services.BuildServiceProvider();
        var             tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, new MemoryCacheTenantProviderCache(tenancy), tenancy);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await providers.CreateServiceProviderAsync(TenantA));
        Assert.Contains("already declares an antiforgery capability", error.Message, StringComparison.Ordinal);
    }

    private static Action<TenantStateBuilder<SchemataTenant>> StandardState() {
        return state => {
            state.CookiePrefix = state.TenantUid == TenantA ? ".a" : ".b";
            state.UseAntiforgery(_ => { });
        };
    }

    private static async Task<IHost> CreateServerAsync(
        Action<TenantStateBuilder<SchemataTenant>> state,
        bool                                       globalFeature = true
    ) {
        var a = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
        var b = new SchemataTenant { Uid = TenantB, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        manager.Setup(value => value.FindByTenantId(TenantB, It.IsAny<CancellationToken>())).ReturnsAsync(b);

        return await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services => {
            services.AddRouting();
            services.AddHttpContextAccessor();
            services.AddSingleton(manager.Object);
            services.AddScoped<ITenantResolver, RequestHeaderResolver>();
            if (globalFeature) {
                var configurators = new Configurators();
                configurators.Set<AntiforgeryOptions>(_ => { });
                new SchemataAntiforgeryFeature().ConfigureServices(services, new(), configurators, new ConfigurationBuilder().Build(), null!);
            } else {
                services.AddDataProtection();
                services.TryAddSingleton<IStatePipelineProvider, DefaultStatePipelineProvider>();
            }
            new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
                .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
            var builder = new SchemataTenancyBuilder<SchemataTenant>(services);
            builder.UseState(state);
        }).Configure(app => {
            app.UseRouting();
            app.UseMiddleware<SchemataTenancyMiddleware<SchemataTenant>>();
            app.UseMiddleware<SchemataTenantPrincipalMiddleware<SchemataTenant>>();
            app.UseMiddleware<TenantExecutionMiddleware>();
            if (globalFeature) {
                new SchemataAntiforgeryFeature().ConfigureApplication(app, new ConfigurationBuilder().Build(), null!);
            } else {
                app.UseStatePipeline(StatePipeline.Antiforgery);
            }
            app.UseEndpoints(endpoints => {
                MapAntiforgery(endpoints, "global");
                MapAntiforgery(endpoints, "A");
                MapAntiforgery(endpoints, "B");
            });
        })).StartAsync();
    }

    private static void MapAntiforgery(IEndpointRouteBuilder endpoints, string scope) {
        endpoints.MapGet($"/{scope}/af/token", context => {
            var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
            return context.Response.WriteAsync(tokens.RequestToken!);
        });
        endpoints.MapPost($"/{scope}/af/metadata", async context => {
            var feature = context.Features.Get<IAntiforgeryValidationFeature>();
            bool valid;
            if (feature is not null) {
                valid = feature.IsValid;
            } else {
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                valid = await antiforgery.IsRequestValidAsync(context);
            }
            if (!valid) {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("rejected");
                return;
            }
            await context.Response.WriteAsync("accepted");
        }).WithMetadata(new RequireAntiforgeryTokenAttribute());
        endpoints.MapPost($"/{scope}/af/direct", async context => {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            try {
                await antiforgery.ValidateRequestAsync(context);
            } catch (AntiforgeryValidationException) {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("rejected");
                return;
            }
            await context.Response.WriteAsync("accepted");
        });
    }

    private static TenantClientHandler Client(IHost host) {
        return new() { InnerHandler = host.GetTestServer().CreateHandler() };
    }

    private static HttpClient CreateClient(TenantClientHandler handler) {
        return new(handler) { BaseAddress = new Uri("http://localhost") };
    }

    private static async Task<(string RequestToken, IReadOnlyList<string> Cookies)> EstablishAsync(HttpClient client, string scope) {
        using var response = await client.GetAsync($"/{scope}/af/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];
        return (await response.Content.ReadAsStringAsync(), cookies);
    }

    private static async Task<HttpStatusCode> SubmitAsync(HttpClient client, string scope, string path, string? requestToken) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/{scope}/af/{path}");
        if (requestToken is not null) request.Headers.Add(HeaderName, requestToken);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private sealed class TenantClientHandler : DelegatingHandler
    {
        private readonly CookieJar _jar = new();

        public CookieJar Jar => _jar;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (path.StartsWith("/A/", StringComparison.Ordinal)) {
                request.Headers.Add("x-tenant-id", TenantA.ToString("D"));
            } else if (path.StartsWith("/B/", StringComparison.Ordinal)) {
                request.Headers.Add("x-tenant-id", TenantB.ToString("D"));
            }
            if (_jar.Header is { } cookie) request.Headers.TryAddWithoutValidation("Cookie", cookie);
            var response = await base.SendAsync(request, cancellationToken);
            _jar.Update(response);
            return response;
        }
    }

    private sealed class CookieJar
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        public string? Header {
            get { return _cookies.Count == 0 ? null : string.Join("; ", _cookies.Select(pair => $"{pair.Key}={pair.Value}")); }
        }

        public string? Get(string name) {
            return _cookies.TryGetValue(name, out var value) ? value : null;
        }

        public void Tamper(string prefix, string value) {
            var name = _cookies.Keys.FirstOrDefault(key => key.StartsWith(prefix, StringComparison.Ordinal));
            Assert.NotNull(name);
            _cookies[name] = value;
        }

        public void Update(HttpResponseMessage response) {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
            foreach (var value in values) {
                var pair = value.Split(';', 2)[0];
                var separator = pair.IndexOf('=');
                if (separator <= 0) continue;
                var name     = pair[..separator].Trim();
                var contents = pair[(separator + 1)..].Trim();
                if (contents.Length == 0) _cookies.Remove(name);
                else _cookies[name] = contents;
            }
        }
    }
}
