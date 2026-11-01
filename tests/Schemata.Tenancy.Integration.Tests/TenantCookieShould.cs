using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Core.Features;
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
public sealed class TenantCookieShould
{
    private const string Scheme   = "tenant";
    private const string CookieA  = ".atenant";
    private const string CookieB  = ".btenant";

    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Cookie_Jar_Keeps_Global_A_And_B_Isolated_Across_SignIn_Authenticate_Renew_And_SignOut() {
        var clockA = new FakeTimeProvider();
        CookieAuthenticationOptions? profileA = null;
        using var host    = await CreateServerAsync(state => {
            state.CookiePrefix = state.TenantUid == TenantA ? ".a" : ".b";
            state.UseCookie(Scheme, options => {
                RejectRedirects(options);
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                if (state.TenantUid == TenantA) {
                    options.TimeProvider = clockA;
                    profileA = options;
                }
            });
        });
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        await SignInAsync(client, "global", "global-user");
        await SignInAsync(client, "A", "alice");
        await SignInAsync(client, "B", "bob");

        Assert.Equal("alice", await WhoAmIAsync(client, "A"));
        Assert.Equal("bob", await WhoAmIAsync(client, "B"));
        Assert.Equal("global-user", await WhoAmIAsync(client, "global"));
        using (var granted = await client.GetAsync("/A/private")) {
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        }

        Assert.NotNull(profileA);
        clockA.Advance(TimeSpan.FromMinutes(8));
        using (var renewed = await client.GetAsync("/A/whoami")) {
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            Assert.True(renewed.Headers.TryGetValues("Set-Cookie", out var setCookies));
            Assert.Contains(setCookies, value => value.StartsWith($"{CookieA}=", StringComparison.Ordinal));
        }
        using (var untouchedB = await client.GetAsync("/B/whoami")) {
            Assert.Equal(HttpStatusCode.OK, untouchedB.StatusCode);
            Assert.False(untouchedB.Headers.Contains("Set-Cookie"));
        }
        using (var untouchedGlobal = await client.GetAsync("/global/whoami")) {
            Assert.Equal(HttpStatusCode.OK, untouchedGlobal.StatusCode);
            Assert.False(untouchedGlobal.Headers.Contains("Set-Cookie"));
        }
        Assert.Equal("alice", await WhoAmIAsync(client, "A"));

        await SignOutAsync(client, "A");
        Assert.Null(handler.Jar.Get(CookieA));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/A/private")).StatusCode);
        Assert.Equal("bob", await WhoAmIAsync(client, "B"));
        Assert.Equal("global-user", await WhoAmIAsync(client, "global"));
    }

    [Fact]
    public async Task Cross_Profile_Ticket_Value_Is_Rejected_By_Sibling_Profile() {
        using var host    = await CreateServerAsync(StandardState());
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        await SignInAsync(client, "A", "alice");
        await SignInAsync(client, "B", "bob");
        var aCookie = handler.Jar.Get(CookieA);
        var bCookie = handler.Jar.Get(CookieB);
        Assert.NotNull(aCookie);
        Assert.NotNull(bCookie);

        handler.Jar.Set(CookieB, aCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/B/whoami")).StatusCode);

        handler.Jar.Set(CookieB, bCookie);
        Assert.Equal("bob", await WhoAmIAsync(client, "B"));
    }

    [Fact]
    public async Task Shared_Ticket_Store_Reference_Stays_Namespaced_Per_Profile() {
        var store = new InMemoryTicketStore();
        using var host    = await CreateServerAsync(state => {
            state.CookiePrefix = state.TenantUid == TenantA ? ".a" : ".b";
            state.UseCookie(Scheme, options => {
                RejectRedirects(options);
                options.SessionStore = store;
            });
        });
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        await SignInAsync(client, "A", "alice");
        await SignInAsync(client, "B", "bob");

        Assert.Equal(2, store.Keys.Count);
        Assert.Equal("alice", await WhoAmIAsync(client, "A"));

        // A's session reference under B's profile never resolves: B's wrapper rejects the
        // foreign prefix before the shared store is consulted.
        var stolen = handler.Jar.Get(CookieA);
        var legit  = handler.Jar.Get(CookieB);
        handler.Jar.Set(CookieB, stolen!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/B/whoami")).StatusCode);
        handler.Jar.Set(CookieB, legit!);
        Assert.Equal("bob", await WhoAmIAsync(client, "B"));

        await SignOutAsync(client, "A");
        Assert.Single(store.Keys);
        Assert.Equal("bob", await WhoAmIAsync(client, "B"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/A/whoami")).StatusCode);
    }

    [Fact]
    public async Task Principal_Only_Request_Authenticates_Global_First_Then_Explicit_Tenant_Scheme() {
        using var host    = await CreateServerAsync(StandardState());
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        await SignInAsync(client, "global", "global-user");
        using (var response = await client.GetAsync("/A/principal")) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("global-user|-", await response.Content.ReadAsStringAsync());
        }

        await SignInAsync(client, "A", "alice");
        using (var response = await client.GetAsync("/A/principal")) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("global-user|alice", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Tenant_Scheme_Operation_Before_Binding_Is_Rejected_Without_A_Host_Handler() {
        InvalidOperationException? observed = null;
        using var host = await CreateServerAsync(
            state => {
                state.CookiePrefix = state.TenantUid == TenantA ? ".a" : ".b";
                state.UseCookie(Scheme, RejectRedirects);
            },
            app => app.Use(async (context, next) => {
                if (context.Request.Path != "/prebind") {
                    await next();
                    return;
                }
                try {
                    await context.AuthenticateAsync(Scheme);
                } catch (InvalidOperationException error) {
                    observed = error;
                }
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync(observed is null ? "authenticated" : "rejected");
            }));
        using var handler = Client(host);
        using var client  = CreateClient(handler);

        using var response = await client.GetAsync("/prebind");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("rejected", await response.Content.ReadAsStringAsync());
        Assert.NotNull(observed);
        Assert.Contains($"No authentication handler is registered for the scheme '{Scheme}'", observed.Message, StringComparison.Ordinal);
        Assert.Contains("The registered schemes are: global", observed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_WithReadOnlyContentRoot_WritesNoFile() {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"schemata-cookie-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        try {
            MakeReadOnly(contentRoot);
            var before = Directory.GetFileSystemEntries(contentRoot);

            var services = new ServiceCollection();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            using var host = new ServiceCollection().BuildServiceProvider();
            var state = new TenantStateBuilder<SchemataTenant>(services, TenantA, host) { CookiePrefix = ".a" };
            state.UseCookie(Scheme, RejectRedirects);

            using var provider = services.BuildServiceProvider();
            var previous = Environment.CurrentDirectory;
            Directory.SetCurrentDirectory(contentRoot);
            try {
                provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme);
            } finally {
                Directory.SetCurrentDirectory(previous);
            }

            Assert.Equal(before, Directory.GetFileSystemEntries(contentRoot));
            Assert.False(File.Exists(Path.Combine(contentRoot, "probe.log")));
        } finally {
            MakeWritable(contentRoot);
            Directory.Delete(contentRoot, true);
        }
    }

    private static Action<TenantStateBuilder<SchemataTenant>> StandardState() {
        return state => {
            state.CookiePrefix = state.TenantUid == TenantA ? ".a" : ".b";
            state.UseCookie(Scheme, RejectRedirects);
        };
    }

    private static void RejectRedirects(CookieAuthenticationOptions options) {
        options.Events.OnRedirectToLogin = context => {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context => {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void MakeReadOnly(string path) {
        if (OperatingSystem.IsWindows()) {
            SetDenyWrite(path);
            return;
        }
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    }

    private static void MakeWritable(string path) {
        if (OperatingSystem.IsWindows()) {
            ClearDenyWrite(path);
            return;
        }
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [SupportedOSPlatform("windows")]
    private static void SetDenyWrite(string path) {
        var info     = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(DenyWriteRule());
        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ClearDenyWrite(string path) {
        var info     = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.RemoveAccessRule(DenyWriteRule());
        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static FileSystemAccessRule DenyWriteRule() {
        return new(WindowsIdentity.GetCurrent().User!, FileSystemRights.Write, AccessControlType.Deny);
    }

    private static async Task<IHost> CreateServerAsync(
        Action<TenantStateBuilder<SchemataTenant>> state,
        Action<IApplicationBuilder>?               beforePrincipal = null
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
            services.AddAuthentication("global").AddCookie("global", RejectRedirects);
            new SchemataAuthenticationFeature().ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
            services.AddAuthorization();
            services.AddScoped<ITenantResolver, RequestHeaderResolver>();
            new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
                .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
            new SchemataTenancyBuilder<SchemataTenant>(services).UseState(state);
        }).Configure(app => {
            app.UseRouting();
            app.UseMiddleware<SchemataTenancyMiddleware<SchemataTenant>>();
            app.UseAuthentication();
            beforePrincipal?.Invoke(app);
            app.UseMiddleware<SchemataTenantPrincipalMiddleware<SchemataTenant>>();
            app.UseAuthorization();
            app.UseMiddleware<TenantExecutionMiddleware>();
            app.UseEndpoints(endpoints => {
                MapScope(endpoints, "global");
                MapScope(endpoints, "A");
                MapScope(endpoints, "B");
            });
        })).StartAsync();
    }

    private static void MapScope(IEndpointRouteBuilder endpoints, string scope) {
        var scheme = scope == "global" ? "global" : Scheme;
        endpoints.MapGet($"/{scope}/signin", async context => {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, context.Request.Query["user"].ToString())], scheme);
            await context.SignInAsync(scheme, new ClaimsPrincipal(identity));
        });
        endpoints.MapGet($"/{scope}/whoami", WhoAmI(scheme));
        endpoints.MapGet($"/{scope}/signout", async context => {
            await context.SignOutAsync(scheme);
        });
        endpoints.MapGet($"/{scope}/private", () => "private").RequireAuthorization(scheme == "global"
            ? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()
            : new AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser().Build());
        if (scope == "A") {
            endpoints.MapGet("/A/principal", async context => {
                var global = await context.AuthenticateAsync();
                var tenant = await context.AuthenticateAsync(Scheme);
                await context.Response.WriteAsync($"{Name(global)}|{Name(tenant)}");
            });
        }
    }

    private static string Name(AuthenticateResult result) {
        return result.Succeeded ? result.Principal?.Identity?.Name ?? "-" : "-";
    }

    private static RequestDelegate WhoAmI(string scheme) {
        return async context => {
            var result = await context.AuthenticateAsync(scheme);
            if (!result.Succeeded) {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await context.Response.WriteAsync(Name(result));
        };
    }

    private static TenantClientHandler Client(IHost host) {
        return new() { InnerHandler = host.GetTestServer().CreateHandler() };
    }

    private static HttpClient CreateClient(TenantClientHandler handler) {
        return new(handler) { BaseAddress = new Uri("http://localhost") };
    }

    private static async Task SignInAsync(HttpClient client, string scope, string name) {
        using var response = await client.GetAsync($"/{scope}/signin?user={Uri.EscapeDataString(name)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task SignOutAsync(HttpClient client, string scope) {
        using var response = await client.GetAsync($"/{scope}/signout");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string?> WhoAmIAsync(HttpClient client, string scope) {
        using var response = await client.GetAsync($"/{scope}/whoami");
        if (response.StatusCode != HttpStatusCode.OK) return null;
        return await response.Content.ReadAsStringAsync();
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

    private sealed class InMemoryTicketStore : ITicketStore
    {
        private readonly ConcurrentDictionary<string, AuthenticationTicket> _tickets = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys {
            get { return _tickets.Keys.ToArray(); }
        }

        public Task<string> StoreAsync(AuthenticationTicket ticket) {
            var key = Guid.NewGuid().ToString("N");
            _tickets[key] = ticket;
            return Task.FromResult(key);
        }

        public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken) {
            return StoreAsync(ticket);
        }

        public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) {
            return StoreAsync(ticket);
        }

        public Task RenewAsync(string key, AuthenticationTicket ticket) {
            _tickets[key] = ticket;
            return Task.CompletedTask;
        }

        public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) {
            return RenewAsync(key, ticket);
        }

        public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) {
            return RenewAsync(key, ticket);
        }

        public Task<AuthenticationTicket?> RetrieveAsync(string key) {
            return Task.FromResult(_tickets.TryGetValue(key, out var ticket) ? ticket : null);
        }

        public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken) {
            return RetrieveAsync(key);
        }

        public Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) {
            return RetrieveAsync(key);
        }

        public Task RemoveAsync(string key) {
            _tickets.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken) {
            return RemoveAsync(key);
        }

        public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) {
            return RemoveAsync(key);
        }
    }
}
