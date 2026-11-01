using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Session;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Caching.Memory;
using Schemata.Caching.Skeleton;
using Schemata.Core.State;
using Schemata.Tenancy.Caching;
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
public sealed class TenantSessionShould
{
    private const string CookieName = ".AspNetCore.Session";
    private const string CookieA    = ".a.AspNetCore.Session";
    private const string CookieB    = ".b.AspNetCore.Session";

    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Session_State_Keeps_Global_A_And_B_Isolated_Across_Write_Read_And_Clear() {
        var backend = new MemoryCacheBackend();
        using var host    = await CreateServerAsync(cache => {
            cache.UseShared(backend);
            cache.UseSessionCache();
        }, state => {
            state.CookiePrefix = state.TenantUid == TenantA ? ".a" : ".b";
            state.UseSession<DistributedSessionStore>(options => options.IdleTimeout = IdleTimeout);
        });
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        var establishedA = await SetSessionAsync(client, "A", "alice");
        Assert.Single(establishedA, value => value.StartsWith(CookieA, StringComparison.Ordinal));
        Assert.DoesNotContain(establishedA, value => value.StartsWith(CookieName, StringComparison.Ordinal));

        var establishedGlobal = await SetSessionAsync(client, "global", "carol");
        Assert.Single(establishedGlobal, value => value.StartsWith(CookieName, StringComparison.Ordinal));
        Assert.DoesNotContain(establishedGlobal, value => value.StartsWith(CookieA, StringComparison.Ordinal));

        var establishedB = await SetSessionAsync(client, "B", "bob");
        Assert.Single(establishedB, value => value.StartsWith(CookieB, StringComparison.Ordinal));

        var sessionId = await SessionIdAsync(client, "A");
        Assert.Equal("alice", await ReadSessionAsync(client, "A"));
        Assert.Equal(sessionId, await SessionIdAsync(client, "A"));

        await ClearSessionAsync(client, "A");

        Assert.Equal("bob", await ReadSessionAsync(client, "B"));
        Assert.Equal("carol", await ReadSessionAsync(client, "global"));
        Assert.Equal(string.Empty, await ReadSessionAsync(client, "A"));
    }

    [Fact]
    public async Task Stolen_Cookie_And_Session_Identifier_Yields_Nothing_Of_Another_Profile() {
        var backend = new MemoryCacheBackend();
        using var host    = await CreateServerAsync(SharedSessionCache(backend), StandardState());
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        await SetSessionAsync(client, "A", "alice");
        await SetSessionAsync(client, "B", "bob");
        var stolen = handler.Jar.Get(CookieA);
        var legit  = handler.Jar.Get(CookieB);
        Assert.NotNull(stolen);
        Assert.NotNull(legit);

        handler.Jar.Set(CookieB, stolen);
        Assert.Equal(string.Empty, await ReadSessionAsync(client, "B"));

        handler.Jar.Set(CookieB, legit);
        Assert.Equal("bob", await ReadSessionAsync(client, "B"));
    }

    [Fact]
    public async Task Idle_Requests_Extend_The_Sliding_Window_Without_Resurrecting_Expired_Data() {
        var clock = new FakeTimeProvider();
        using var host    = await CreateServerAsync(cache => {
            cache.UseDedicated(_ => new MemoryCacheBackend(clock));
            cache.UseSessionCache();
        }, StandardState());
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        await SetSessionAsync(client, "A", "alice");

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal("alice", await ReadSessionAsync(client, "A"));

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal("alice", await ReadSessionAsync(client, "A"));

        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(string.Empty, await ReadSessionAsync(client, "A"));
    }

    [Fact]
    public async Task Exception_Endpoint_Commits_In_Finally_And_Does_Not_Leak_The_Tenant_Store() {
        var backend = new MemoryCacheBackend();
        using var host    = await CreateServerAsync(SharedSessionCache(backend), StandardState());
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        await SetSessionAsync(client, "A", "alice");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => FailSessionAsync(client, "A"));
        Assert.Contains("session endpoint failure", error.Message, StringComparison.Ordinal);

        Assert.Equal("doomed", await ReadSessionAsync(client, "A"));

        Assert.Equal(string.Empty, await ReadSessionAsync(client, "B"));
        Assert.Equal(string.Empty, await ReadSessionAsync(client, "global"));
    }

    [Fact]
    public async Task Dedicated_Backend_Instances_Keep_Counting_State_Isolated_Per_Tenant() {
        using var host    = await CreateServerAsync(cache => {
            cache.UseDedicated(_ => new MemoryCacheBackend());
            cache.UseSessionCache();
        }, StandardState());
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        Assert.Equal("1", await CountSessionAsync(client, "A"));
        Assert.Equal("2", await CountSessionAsync(client, "A"));
        Assert.Equal("1", await CountSessionAsync(client, "B"));
        Assert.Equal("3", await CountSessionAsync(client, "A"));

        await ClearSessionAsync(client, "A");

        Assert.Equal("1", await CountSessionAsync(client, "A"));
        Assert.Equal("2", await CountSessionAsync(client, "B"));
    }

    [Fact]
    public async Task SessionCache_Frames_Session_Keys_Away_From_Business_Keys_Per_Tenant() {
        var backend = new MemoryCacheBackend();
        using var providerA = ComposeSessionProvider(TenantA, backend);
        using var providerB = ComposeSessionProvider(TenantB, backend);

        var sessionsA = Assert.IsType<SessionCache>(providerA.GetRequiredService<IDistributedCache>());
        var sessionsB = Assert.IsType<SessionCache>(providerB.GetRequiredService<IDistributedCache>());
        var businessA = providerA.GetRequiredService<ICacheProvider>();

        await sessionsA.SetAsync("shared", new byte[] { 1 }, new DistributedCacheEntryOptions());
        await sessionsB.SetAsync("shared", new byte[] { 2 }, new DistributedCacheEntryOptions());

        Assert.Equal(new byte[] { 1 }, await sessionsA.GetAsync("shared"));
        Assert.Equal(new byte[] { 2 }, await sessionsB.GetAsync("shared"));

        await businessA.SetAsync("business-shared", new byte[] { 9 }, new CacheEntryOptions());

        Assert.Equal(new byte[] { 1 }, await sessionsA.GetAsync("shared"));
        Assert.Equal(new byte[] { 9 }, await businessA.GetAsync("business-shared"));
    }

    [Fact]
    public async Task SessionCache_Options_Map_Verbatim_And_Refresh_Preserves_The_Absolute_Ceiling() {
        var clock = new FakeTimeProvider();
        using var provider = ComposeSessionProvider(TenantA, new MemoryCacheBackend(clock));
        var sessions = Assert.IsType<SessionCache>(provider.GetRequiredService<IDistributedCache>());

        await sessions.SetAsync("bounded", new byte[] { 7 }, new DistributedCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
            SlidingExpiration               = TimeSpan.FromMinutes(1),
        });

        clock.Advance(TimeSpan.FromSeconds(30));
        await sessions.RefreshAsync("bounded");

        clock.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal(new byte[] { 7 }, await sessions.GetAsync("bounded"));

        clock.Advance(TimeSpan.FromSeconds(40));
        Assert.Null(await sessions.GetAsync("bounded"));
    }

    [Fact]
    public async Task Synchronous_Members_Reject_An_Asynchronous_Backend_Without_Blocking_The_Request_Thread() {
        var pending = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Mock<ICacheBackend>();
        backend.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(pending.Task);
        using var provider = ComposeSessionProvider(TenantA, backend.Object);
        var sessions = Assert.IsType<SessionCache>(provider.GetRequiredService<IDistributedCache>());

        var read = sessions.GetAsync("slow");
        Assert.False(read.IsCompleted);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => Task.Run(() => sessions.Get("slow")).WaitAsync(TimeSpan.FromSeconds(30)));

        pending.SetResult(new byte[] { 3 });
        Assert.Equal(new byte[] { 3 }, await read);
    }

    [Fact]
    public async Task Asynchronous_Members_Propagate_Cancellation_To_The_Backend() {
        var backend = new Mock<ICacheBackend>();
        backend.Setup(value => value.SetAsync(
                          It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                          It.IsAny<CancellationToken>()))
               .Returns((string _, byte[] _, CacheEntryOptions _, CancellationToken ct) => Delay(ct));
        using var provider = ComposeSessionProvider(TenantA, backend.Object);
        var sessions = Assert.IsType<SessionCache>(provider.GetRequiredService<IDistributedCache>());
        using var cts = new CancellationTokenSource();

        var write = sessions.SetAsync("cancelled", new byte[] { 1 }, new DistributedCacheEntryOptions(), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
    }

    [Fact]
    public async Task Synchronous_Members_Keep_The_Tenant_Namespace_And_Agree_With_Asynchronous_Members() {
        var backend = new MemoryCacheBackend();
        using var providerA = ComposeSessionProvider(TenantA, backend);
        using var providerB = ComposeSessionProvider(TenantB, backend);
        var sessionsA = Assert.IsType<SessionCache>(providerA.GetRequiredService<IDistributedCache>());
        var sessionsB = Assert.IsType<SessionCache>(providerB.GetRequiredService<IDistributedCache>());
        var businessA = providerA.GetRequiredService<ICacheProvider>();

        sessionsA.Set("shared", new byte[] { 1 }, new DistributedCacheEntryOptions());
        sessionsB.Set("shared", new byte[] { 2 }, new DistributedCacheEntryOptions());
        await businessA.SetAsync("shared", new byte[] { 9 }, new CacheEntryOptions());

        Assert.Equal(new byte[] { 1 }, sessionsA.Get("shared"));
        Assert.Equal(new byte[] { 2 }, sessionsB.Get("shared"));
        Assert.Equal(new byte[] { 1 }, await sessionsA.GetAsync("shared"));
        Assert.Equal(new byte[] { 9 }, await businessA.GetAsync("shared"));
    }

    [Fact]
    public async Task Composite_Resolution_Keeps_The_Tenant_Synchronous_Binding_Ahead_Of_A_Host_Capability() {
        var backend = new Mock<ICacheBackend>();
        var host = new Mock<ISynchronousCacheBackend>(MockBehavior.Strict);
        var tenant = new SchemataTenant { Uid = TenantA, Timestamp = Guid.NewGuid() };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(TenantA, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var settings = new SchemataTenancyOptions { ProviderMaxCapacity = 10 };
        settings.DynamicOverrides.Add((_, services, _) => {
            var cache = new TenantCacheBuilder<SchemataTenant>(services, TenantA);
            cache.UseShared(backend.Object);
            cache.UseSessionCache();
        });
        var options = Options.Create(settings);

        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.AddSingleton(host.Object);
        using var root = services.BuildServiceProvider();
        using var providers = new MemoryCacheTenantProviderCache(options);
        var factory = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, providers, options);

        await using var lease = await factory.CreateServiceProviderAsync(TenantA);
        var sessions = Assert.IsType<SessionCache>(lease.Provider.GetRequiredService<IDistributedCache>());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => Task.Run(() => sessions.Get("shared")).WaitAsync(TimeSpan.FromSeconds(30)));
        host.VerifyNoOtherCalls();
    }

    private static async Task Delay(CancellationToken ct) {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    private static Action<TenantCacheBuilder<SchemataTenant>> SharedSessionCache(MemoryCacheBackend backend) {
        return cache => {
            cache.UseShared(backend);
            cache.UseSessionCache();
        };
    }

    private static Action<TenantStateBuilder<SchemataTenant>> StandardState() {
        return state => {
            state.CookiePrefix = state.TenantUid == TenantA ? ".a" : ".b";
            state.UseSession<DistributedSessionStore>(options => options.IdleTimeout = IdleTimeout);
        };
    }

    private static ServiceProvider ComposeSessionProvider(Guid tenant, ICacheBackend backend) {
        var services = new ServiceCollection();
        var builder  = new TenantCacheBuilder<SchemataTenant>(services, tenant);
        builder.UseShared(backend);
        builder.UseSessionCache();
        return services.BuildServiceProvider();
    }

    private static async Task<IHost> CreateServerAsync(
        Action<TenantCacheBuilder<SchemataTenant>> cache,
        Action<TenantStateBuilder<SchemataTenant>> state
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
            services.AddDistributedMemoryCache();
            services.AddSession();
            services.TryAddSingleton<IStatePipelineProvider, DefaultStatePipelineProvider>();
            new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
                .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
            var builder = new SchemataTenancyBuilder<SchemataTenant>(services);
            builder.UseCache(cache);
            builder.UseState(state);
        }).Configure(app => {
            app.UseRouting();
            app.UseMiddleware<SchemataTenancyMiddleware<SchemataTenant>>();
            app.UseMiddleware<SchemataTenantPrincipalMiddleware<SchemataTenant>>();
            app.UseMiddleware<TenantExecutionMiddleware>();
            app.UseStatePipeline(StatePipeline.Session);
            app.UseEndpoints(endpoints => {
                MapSession(endpoints, "global");
                MapSession(endpoints, "A");
                MapSession(endpoints, "B");
            });
        })).StartAsync();
    }

    private static void MapSession(IEndpointRouteBuilder endpoints, string scope) {
        endpoints.MapGet($"/{scope}/session/set", async context => {
            context.Session.SetString("user", context.Request.Query["value"].ToString());
            await context.Response.WriteAsync("ok");
        });
        endpoints.MapGet($"/{scope}/session/read", async context => {
            await context.Response.WriteAsync(context.Session.GetString("user") ?? string.Empty);
        });
        endpoints.MapGet($"/{scope}/session/count", async context => {
            var count = int.TryParse(context.Session.GetString("count"), out var parsed) ? parsed + 1 : 1;
            context.Session.SetString("count", count.ToString());
            await context.Response.WriteAsync(count.ToString());
        });
        endpoints.MapGet($"/{scope}/session/clear", async context => {
            context.Session.Clear();
            await context.Response.WriteAsync("ok");
        });
        endpoints.MapGet($"/{scope}/session/id", async context => {
            await context.Response.WriteAsync(context.Session.Id);
        });
        endpoints.MapGet($"/{scope}/session/fail", context => {
            context.Session.SetString("user", "doomed");
            throw new InvalidOperationException("session endpoint failure");
        });
    }

    private static TenantClientHandler Client(IHost host) {
        return new() { InnerHandler = host.GetTestServer().CreateHandler() };
    }

    private static HttpClient CreateClient(TenantClientHandler handler) {
        return new(handler) { BaseAddress = new Uri("http://localhost") };
    }

    private static async Task<IReadOnlyList<string>> SetSessionAsync(HttpClient client, string scope, string value) {
        using var response = await client.GetAsync($"/{scope}/session/set?value={Uri.EscapeDataString(value)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.ToArray() : [];
    }

    private static async Task<string> ReadSessionAsync(HttpClient client, string scope) {
        using var response = await client.GetAsync($"/{scope}/session/read");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> CountSessionAsync(HttpClient client, string scope) {
        using var response = await client.GetAsync($"/{scope}/session/count");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task ClearSessionAsync(HttpClient client, string scope) {
        using var response = await client.GetAsync($"/{scope}/session/clear");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> SessionIdAsync(HttpClient client, string scope) {
        using var response = await client.GetAsync($"/{scope}/session/id");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task FailSessionAsync(HttpClient client, string scope) {
        await client.GetAsync($"/{scope}/session/fail");
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

        public void Set(string name, string value) {
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
