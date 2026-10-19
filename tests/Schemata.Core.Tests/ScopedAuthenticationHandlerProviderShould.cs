using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Core.State;
using Xunit;

namespace Schemata.Core.Tests;

[Trait("Layer", "Unit")]
public sealed class ScopedAuthenticationHandlerProviderShould
{
    private const string SchemeName = "probe";

    [Fact]
    public async Task Create_And_Initialize_The_Handler_From_The_Explicit_Provider() {
        var       origin        = new HandlerOrigin();
        using var explicitScope = new ServiceCollection().AddSingleton(origin).BuildServiceProvider();
        var       ambient       = new Mock<IServiceProvider>();
        var       context       = new DefaultHttpContext { RequestServices = ambient.Object };
        var       provider      = new ScopedAuthenticationHandlerProvider(explicitScope, Schemes());

        var handler = Assert.IsType<ProbeHandler>(await provider.GetHandlerAsync(context, SchemeName));

        Assert.Same(origin, handler.Origin);
        Assert.Same(context, handler.Context);
        Assert.Equal(SchemeName, handler.Scheme?.Name);
        ambient.Verify(value => value.GetService(It.IsAny<Type>()), Times.Never);
        ambient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Return_One_Handler_For_Concurrent_First_Lookups() {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        schemes.Setup(value => value.GetSchemeAsync(SchemeName))
            .Returns(async () => {
                await gate.Task;
                return new AuthenticationScheme(SchemeName, null, typeof(ProbeHandler));
            });

        using var explicitScope = new ServiceCollection().AddSingleton(new HandlerOrigin()).BuildServiceProvider();
        var       provider      = new ScopedAuthenticationHandlerProvider(explicitScope, schemes.Object);
        var       context       = new DefaultHttpContext { RequestServices = explicitScope };

        var first  = provider.GetHandlerAsync(context, SchemeName);
        var second = provider.GetHandlerAsync(context, SchemeName);
        gate.SetResult();

        var handlers = await Task.WhenAll(first, second);

        Assert.Same(handlers[0], handlers[1]);
        schemes.Verify(value => value.GetSchemeAsync(SchemeName), Times.Once);
    }

    private static IAuthenticationSchemeProvider Schemes() {
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        schemes.Setup(value => value.GetSchemeAsync(SchemeName))
            .ReturnsAsync(new AuthenticationScheme(SchemeName, null, typeof(ProbeHandler)));
        return schemes.Object;
    }

    private sealed class HandlerOrigin;

    private sealed class ProbeHandler(HandlerOrigin origin) : IAuthenticationHandler
    {
        public HandlerOrigin Origin { get; } = origin;

        public AuthenticationScheme? Scheme { get; private set; }

        public HttpContext? Context { get; private set; }

        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) {
            Scheme  = scheme;
            Context = context;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync() {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        public Task ChallengeAsync(AuthenticationProperties? properties) {
            return Task.CompletedTask;
        }

        public Task ForbidAsync(AuthenticationProperties? properties) {
            return Task.CompletedTask;
        }
    }
}
