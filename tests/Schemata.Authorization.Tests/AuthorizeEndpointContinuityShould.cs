using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Proves an <see cref="AuthorizeEndpointRequest" /> dispatched through the real
///     <see cref="InProcessRequestDispatcher" /> runs the endpoint handler on the dispatch's ambient
///     <see cref="AdviceContext" />: a marker stamped by a pipeline advisor around the dispatch must be
///     visible to the <see cref="IAuthorizeAdvisor{TApp}" /> pipeline inside the handler.
/// </summary>
public sealed class AuthorizeEndpointContinuityShould
{
    [Fact]
    public async Task Dispatch_Continues_The_Ambient_Context_Into_The_Authorize_Advisor() {
        var marker   = new MarkerPipelineAdvisor();
        var observer = new ObservingAuthorizeAdvisor();
        var tokens   = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _) => token);

        var options = new SchemataAuthorizationOptions();
        options.AddEphemeralSigningKey();
        options.Issuer         = "https://localhost";
        options.InteractionUri = "https://localhost/consent";

        var services = new ServiceCollection();
        services.AddSingleton<IRequestPipelineAdvisor<AuthorizeEndpointRequest, AuthorizationResult>>(marker);
        services.AddSingleton<IAuthorizeAdvisor<SchemataApplication>>(observer);
        services.AddSingleton<IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>>(
            new EndpointHandler(
                tokens.Object,
                new TokenService(Options.Create(options)),
                Options.Create(options),
                Options.Create(new JsonSerializerOptions())));
        using var provider   = services.BuildServiceProvider();
        var       dispatcher = new InProcessRequestDispatcher(provider);

        var result = await dispatcher.SendAsync<AuthorizeEndpointRequest, AuthorizationResult>(
            new(new AuthorizeRequest {
                    ClientId     = "test",
                    ResponseType = "code",
                    RedirectUri  = "https://client.example/callback",
                },
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(IdentityClaims.Subject, "user-1")], "test"))),
            CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Redirect, result.Status);
        Assert.NotNull(observer.Context);
        Assert.True(observer.Context.TryGet<Marker>(out var value));
        Assert.Same(marker.Value, value);
    }

    private sealed class EndpointHandler(
        ITokenManager<SchemataToken>         tokens,
        TokenService                         issuer,
        IOptions<SchemataAuthorizationOptions> options,
        IOptions<JsonSerializerOptions>      json
    ) : IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>
    {
        public Task<AuthorizationResult> HandleAsync(AuthorizeEndpointRequest request, CancellationToken ct = default)
            => new AuthorizeHandler<SchemataApplication, SchemataToken>(tokens, issuer, options, json)
              .AuthorizeAsync(request.Request, request.Principal!, ct);
    }

    /// <summary>Stamps a <see cref="Marker" /> onto the dispatch's ambient context.</summary>
    private sealed class MarkerPipelineAdvisor : IRequestPipelineAdvisor<AuthorizeEndpointRequest, AuthorizationResult>
    {
        public Marker Value { get; } = new();

        public int Order => 0;

        public Task<AuthorizationResult> AdviseAsync(
            AdviceContext                              ctx,
            AuthorizeEndpointRequest                   request,
            RequestHandlerContinuation<AuthorizationResult> next,
            CancellationToken                          ct = default
        ) {
            ctx.Set(Value);
            return next(ct);
        }
    }

    private sealed class ObservingAuthorizeAdvisor : IAuthorizeAdvisor<SchemataApplication>
    {
        public int Order => 0;

        public AdviceContext? Context { get; private set; }

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                      ctx,
            AuthorizeContext<SchemataApplication> authz,
            CancellationToken                  ct = default
        ) {
            Context          = ctx;
            authz.Application = new() { CanonicalName = "applications/test" };
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed record Marker;
}
