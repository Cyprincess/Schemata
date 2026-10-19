using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Session;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using Moq;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Core.State;
using Xunit;

namespace Schemata.Core.Tests;

[Trait("Layer", "Integration")]
public sealed class StatePipelineShould
{
    [Fact]
    public async Task Commit_Session_After_Endpoint_Through_The_Default_Global_Branch() {
        using var host = await NewSchemataHostAsync(
            builder => builder.UseSession<DistributedSessionStore>(),
            null,
            endpoints => {
                endpoints.MapGet("/session/increment", async context => {
                    context.Session.SetString("count", "1");
                    await context.Response.WriteAsync("incremented");
                });
                endpoints.MapGet("/session/read", async context => {
                    await context.Response.WriteAsync(context.Session.GetString("count") ?? string.Empty);
                });
            });

        using var client = NewCookieClient(host);

        using (var response = await client.GetAsync("/session/increment")) {
            response.EnsureSuccessStatusCode();
            Assert.Equal("incremented", await response.Content.ReadAsStringAsync());
            Assert.True(response.Headers.Contains("Set-Cookie"), "the session commit should establish the session cookie");
        }

        using var next = await client.GetAsync("/session/read");
        next.EnsureSuccessStatusCode();
        Assert.Equal("1", await next.Content.ReadAsStringAsync());
        await host.StopAsync();
    }

    [Fact]
    public async Task Clear_Session_Feature_On_The_Exception_Path() {
        var configuration = new ConfigurationBuilder().Build();
        var installed     = false;
        var cleared       = false;

        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services => {
                services.AddDistributedMemoryCache();
                services.AddRouting();
                var builder = new SchemataBuilder(configuration, null!);
                builder.UseSession<DistributedSessionStore>();
                builder.Invoke(services);
            })
            .Configure(app => {
                app.Use(async (HttpContext context, Func<Task> next) => {
                    try {
                        await next();
                    }
                    catch (InvalidOperationException error) when (error.Message == "endpoint-failure") {
                        cleared = context.Features.Get<ISessionFeature>() is null;
                        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        await context.Response.WriteAsync("failed");
                    }
                });
                new SchemataSessionFeature<DistributedSessionStore>().ConfigureApplication(app, configuration, null!);
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGet("/session/throw", context => {
                    installed = context.Features.Get<ISessionFeature>() is not null;
                    context.Session.SetString("count", "1");
                    throw new InvalidOperationException("endpoint-failure");
                }));
            })).StartAsync();

        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/session/throw");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("failed", await response.Content.ReadAsStringAsync());
        Assert.True(installed, "the session feature should be installed while the endpoint runs");
        Assert.True(cleared, "the session feature should be cleared when the exception crosses the branch");
        await host.StopAsync();
    }

    [Fact]
    public async Task Skip_Backend_Resolution_Without_A_State_Capability() {
        var store = new Mock<ISessionStore>();

        using var host = await NewSchemataHostAsync(
            null,
            services => services.AddSingleton(store.Object),
            endpoints => endpoints.MapGet("/session/probe", async context => {
                try {
                    _ = context.Session.GetString("count");
                    await context.Response.WriteAsync("session");
                }
                catch (InvalidOperationException) {
                    await context.Response.WriteAsync("no-session");
                }
            }));

        using var client   = host.GetTestClient();
        using var response = await client.GetAsync("/session/probe");
        response.EnsureSuccessStatusCode();
        Assert.Equal("no-session", await response.Content.ReadAsStringAsync());
        store.VerifyNoOtherCalls();
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IStatePipelineProvider>());
        await host.StopAsync();
    }

    [Fact]
    public async Task Keep_Concurrent_Global_And_Profile_Requests_On_Distinct_State_Objects() {
        using var profileServices = new ServiceCollection()
            .AddLogging()
            .AddDistributedMemoryCache()
            .AddSession()
            .BuildServiceProvider();

        var observations = new ConcurrentBag<(string SessionId, object Session, string Profile)>();

        using var globalHost = await NewSchemataHostAsync(
            builder => builder.UseSession<DistributedSessionStore>(),
            null,
            endpoints => MapSessionProbe(endpoints, "global", observations));

        using var profileHost = await NewSchemataHostAsync(
            builder => builder.UseSession<DistributedSessionStore>(),
            services => services.AddSingleton<IStatePipelineProvider>(new DefaultStatePipelineProvider(profileServices)),
            endpoints => MapSessionProbe(endpoints, "profile", observations));

        await Task.WhenAll(
            Enumerable.Range(0, 6).Select(index => SetSessionAsync(globalHost, $"g{index}"))
                .Concat(Enumerable.Range(0, 6).Select(index => SetSessionAsync(profileHost, $"p{index}"))));

        Assert.Equal(12, observations.Count);
        Assert.Equal(12, observations.Select(observation => observation.Session).Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(6, observations.Where(observation => observation.Profile == "global").Select(observation => observation.SessionId).Distinct().Count());
        Assert.Equal(6, observations.Where(observation => observation.Profile == "profile").Select(observation => observation.SessionId).Distinct().Count());

        using (var global = NewCookieClient(globalHost)) {
            using (var set = await global.GetAsync("/session/set?value=final-global")) set.EnsureSuccessStatusCode();
            using (var read = await global.GetAsync("/session/read")) {
                read.EnsureSuccessStatusCode();
                Assert.Equal("final-global", await read.Content.ReadAsStringAsync());
            }
        }

        using (var profile = NewCookieClient(profileHost)) {
            using (var set = await profile.GetAsync("/session/set?value=final-profile")) set.EnsureSuccessStatusCode();
            using (var read = await profile.GetAsync("/session/read")) {
                read.EnsureSuccessStatusCode();
                Assert.Equal("final-profile", await read.Content.ReadAsStringAsync());
            }
        }

        await globalHost.StopAsync();
        await profileHost.StopAsync();
    }

    private static Task<IHost> NewSchemataHostAsync(
        Action<SchemataBuilder>?      schema,
        Action<IServiceCollection>?   services,
        Action<IEndpointRouteBuilder> map
    ) {
        return new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices((context, collection) => {
                services?.Invoke(collection);
                collection.AddDistributedMemoryCache();
                collection.AddSchemata(context.Configuration, context.HostingEnvironment, builder => {
                    builder.UseRouting();
                    schema?.Invoke(builder);
                });
            })
            .Configure(app => app.UseEndpoints(map))).StartAsync();
    }

    private static void MapSessionProbe(
        IEndpointRouteBuilder                                   endpoints,
        string                                                  profile,
        ConcurrentBag<(string SessionId, object Session, string Profile)> observations
    ) {
        endpoints.MapGet("/session/set", async context => {
            var session = context.Features.Get<ISessionFeature>()!.Session!;
            observations.Add((session.Id, session, profile));
            context.Session.SetString("owner", context.Request.Query["value"].ToString());
            await context.Response.WriteAsync(session.Id);
        });
        endpoints.MapGet("/session/read", async context => {
            await context.Response.WriteAsync(context.Session.GetString("owner") ?? string.Empty);
        });
    }

    private static HttpClient NewCookieClient(IHost host) {
        var server = host.GetTestServer();
        return new HttpClient(new CookieHandler { InnerHandler = server.CreateHandler() }) {
            BaseAddress = server.BaseAddress,
        };
    }

    private sealed class CookieHandler : DelegatingHandler
    {
        private readonly CookieContainer _container = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var cookie = _container.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(cookie)) {
                request.Headers.TryAddWithoutValidation(HeaderNames.Cookie, cookie);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)) {
                foreach (var value in values) {
                    _container.SetCookies(request.RequestUri!, value);
                }
            }

            return response;
        }
    }

    private static async Task<string> SetSessionAsync(IHost host, string value) {
        using var client   = host.GetTestClient();
        using var response = await client.GetAsync($"/session/set?value={value}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
