using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Tenancy.Foundation.State;
using Xunit;

namespace Schemata.Tenancy.Tests;

[Trait("Layer", "Unit")]
public sealed class ScopedAuthenticationServiceShould
{
    private const string HostScheme       = "host";
    private const string TenantScheme     = "tenant";
    private const string UnresolvedScheme = "unresolved";

    private const string TenantKey = "test.tenant.native";

    [Fact]
    public async Task Route_Tenant_And_Host_Schemes_Through_Their_Owning_Scope() {
        using var fixture = new Fixture();
        var       context = fixture.Context;

        var tenant = await context.AuthenticateAsync(TenantScheme);
        var host   = await context.AuthenticateAsync(HostScheme);

        Assert.Equal("tenant", tenant.Principal?.Identity?.Name);
        Assert.Equal("host", host.Principal?.Identity?.Name);
        Assert.Same(fixture.RequestServices, context.RequestServices);
        Assert.All(fixture.Observations, observed => Assert.Same(fixture.RequestServices, observed.Services));
    }

    [Fact]
    public async Task Keep_The_Request_Provider_While_Host_And_Tenant_Authenticate_Concurrently() {
        var       gate    = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(gate.Task);
        var       context = fixture.Context;

        var tenant = context.AuthenticateAsync(TenantScheme);
        var host   = context.AuthenticateAsync(HostScheme);
        await fixture.BothStarted.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, fixture.Observations.Count);

        gate.SetResult();
        var results = await Task.WhenAll(tenant, host);

        Assert.Equal("tenant", results[0].Principal?.Identity?.Name);
        Assert.Equal("host", results[1].Principal?.Identity?.Name);
        Assert.Same(fixture.RequestServices, context.RequestServices);
        Assert.All(fixture.Observations, observed => Assert.Same(fixture.RequestServices, observed.Services));
    }

    [Fact]
    public async Task Forward_Challenge_Forbid_SignIn_And_SignOut_To_The_Owning_Scope() {
        using var fixture    = new Fixture();
        var       context    = fixture.Context;
        var       properties = new AuthenticationProperties();
        var       principal  = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test"));

        await context.ChallengeAsync(TenantScheme, properties);
        await context.ForbidAsync(HostScheme, properties);
        await context.SignInAsync(TenantScheme, principal, properties);
        await context.SignOutAsync(HostScheme, properties);

        Assert.Same(fixture.RequestServices, context.RequestServices);

        var tenantCalls = fixture.Observations.Where(observed => observed.Identity == "tenant").ToArray();
        Assert.Collection(tenantCalls,
            challenge => {
                Assert.Equal("challenge", challenge.Operation);
                Assert.Equal(TenantScheme, challenge.Scheme);
                Assert.Same(properties, challenge.Properties);
            },
            signin => {
                Assert.Equal("signin", signin.Operation);
                Assert.Equal(TenantScheme, signin.Scheme);
                Assert.Same(principal, signin.Principal);
                Assert.Same(properties, signin.Properties);
            });

        var hostCalls = fixture.Observations.Where(observed => observed.Identity == "host").ToArray();
        Assert.Collection(hostCalls,
            forbid => {
                Assert.Equal("forbid", forbid.Operation);
                Assert.Equal(HostScheme, forbid.Scheme);
                Assert.Same(properties, forbid.Properties);
            },
            signout => {
                Assert.Equal("signout", signout.Operation);
                Assert.Equal(HostScheme, signout.Scheme);
                Assert.Same(properties, signout.Properties);
            });
    }

    [Fact]
    public async Task Report_A_Scheme_Whose_Scope_Does_Not_Register_The_Native_Service() {
        using var fixture = new Fixture();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Context.AuthenticateAsync(UnresolvedScheme));

        Assert.Contains(TenantKey, error.Message, StringComparison.Ordinal);
    }

    private sealed record Observation(
        string                    Identity,
        string                    Operation,
        string?                   Scheme,
        IServiceProvider          Services,
        AuthenticationProperties? Properties,
        ClaimsPrincipal?          Principal
    );

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider      _host;
        private readonly ServiceProvider      _tenant;
        private readonly IServiceScope        _hostScope;
        private readonly IServiceScope        _requestScope;
        private readonly List<Observation>    _observations = [];
        private readonly TaskCompletionSource _bothStarted  = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private          int                  _started;

        public Fixture(Task? gate = null) {
            Gate = gate ?? Task.CompletedTask;

            var host   = new Mock<IAuthenticationService>();
            var tenant = new Mock<IAuthenticationService>();
            Configure(host, "host");
            Configure(tenant, "tenant");

            _host = new ServiceCollection()
                .AddKeyedScoped<IAuthenticationService>(ScopedAuthenticationService.HostServiceKey, (_, _) => host.Object)
                .BuildServiceProvider();
            _hostScope = _host.CreateScope();

            var router = new Mock<IAuthenticationScopeProvider>();
            router.Setup(value => value.GetScope(It.IsAny<HttpContext>(), HostScheme))
                .Returns((HttpContext _, string _) => new AuthenticationScope(
                    _hostScope.ServiceProvider, ScopedAuthenticationService.HostServiceKey));
            router.Setup(value => value.GetScope(It.IsAny<HttpContext>(), TenantScheme))
                .Returns((HttpContext context, string _) => new AuthenticationScope(context.RequestServices, TenantKey));
            router.Setup(value => value.GetScope(It.IsAny<HttpContext>(), UnresolvedScheme))
                .Returns((HttpContext _, string _) => new AuthenticationScope(_hostScope.ServiceProvider, TenantKey));

            _tenant = new ServiceCollection()
                .AddKeyedScoped<IAuthenticationService>(TenantKey, (_, _) => tenant.Object)
                .AddSingleton(router.Object)
                .AddScoped<IAuthenticationService, ScopedAuthenticationService>()
                .BuildServiceProvider();
            _requestScope = _tenant.CreateScope();
            RequestServices = _requestScope.ServiceProvider;
            Context         = new DefaultHttpContext { RequestServices = RequestServices };
        }

        private Task Gate { get; }

        public HttpContext Context { get; }

        public IServiceProvider RequestServices { get; }

        public Task BothStarted => _bothStarted.Task;

        public IReadOnlyList<Observation> Observations {
            get {
                lock (_observations) {
                    return [.. _observations];
                }
            }
        }

        public void Dispose() {
            _requestScope.Dispose();
            _hostScope.Dispose();
            _tenant.Dispose();
            _host.Dispose();
        }

        private void Configure(Mock<IAuthenticationService> service, string identity) {
            service.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string?>()))
                .Returns<HttpContext, string?>(async (context, scheme) => {
                    Record(context, identity, "authenticate", scheme, null, null);
                    await Gate;
                    Record(context, identity, "authenticate-returned", scheme, null, null);

                    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, identity)], scheme));
                    return AuthenticateResult.Success(new AuthenticationTicket(principal, scheme!));
                });
            service.Setup(value => value.ChallengeAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
                .Returns<HttpContext, string?, AuthenticationProperties?>((context, scheme, properties) => {
                    Record(context, identity, "challenge", scheme, properties, null);
                    return Task.CompletedTask;
                });
            service.Setup(value => value.ForbidAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
                .Returns<HttpContext, string?, AuthenticationProperties?>((context, scheme, properties) => {
                    Record(context, identity, "forbid", scheme, properties, null);
                    return Task.CompletedTask;
                });
            service.Setup(value => value.SignInAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()))
                .Returns<HttpContext, string?, ClaimsPrincipal, AuthenticationProperties?>((context, scheme, principal, properties) => {
                    Record(context, identity, "signin", scheme, properties, principal);
                    return Task.CompletedTask;
                });
            service.Setup(value => value.SignOutAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
                .Returns<HttpContext, string?, AuthenticationProperties?>((context, scheme, properties) => {
                    Record(context, identity, "signout", scheme, properties, null);
                    return Task.CompletedTask;
                });
        }

        private void Record(
            HttpContext               context,
            string                    identity,
            string                    operation,
            string?                   scheme,
            AuthenticationProperties? properties,
            ClaimsPrincipal?          principal
        ) {
            lock (_observations) {
                _observations.Add(new(identity, operation, scheme, context.RequestServices, properties, principal));
            }

            if (operation == "authenticate" && Interlocked.Increment(ref _started) == 2) {
                _bothStarted.TrySetResult();
            }
        }
    }
}
