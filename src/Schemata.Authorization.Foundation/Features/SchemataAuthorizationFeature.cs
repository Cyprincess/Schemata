using System.Threading;
using Schemata.Authorization.Foundation.Controllers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Transport.Http.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Configures the Schemata Authorization server: options validation, managers,
///     authentication schemes, claim advisors, the discovery handler, the OAuth
///     model binder, and delegates to registered <see cref="IAuthorizationFlowFeature" />s.
///     Maps the OIDC/OAuth authorization-server metadata and JWKS endpoints at the paths the
///     configured issuer derives, per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderConfig">
///         OpenID Connect Discovery 1.0 §§4-4.1: OpenID Provider Configuration Request
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8414.html#section-3.1">
///         RFC 8414: OAuth 2.0 Authorization Server Metadata §3.1: Authorization Server Metadata Request
///     </seealso>
///     .
/// </summary>
[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataAuthorizationFeature<TApp, TAuth, TScope> : FeatureBase
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
    where TScope : SchemataScope
{
    public const int DefaultPriority = Orders.Extension + 60_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.PopOrDefault<SchemataAuthorizationOptions>();

        services.AddSchemataAuthorizationOptions(configure);
        services.AddSchemataAuthorizationFlows(schemata, configurators);
        services.AddSchemataApplicationPart<SchemataAuthorizationFeature<TApp, TAuth, TScope>>();
        services.AddSchemataAuthorization<TApp, TAuth, TScope>();
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        var server = app.ApplicationServices.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value;
        var issuer = CanonicalIssuer.Validate(server.Issuer);

        // OIDC Discovery §4.2 and RFC 8414 §3.1 share one composer; OIDC additionally
        // requires ID-token signing metadata.
        async Task<IResult> Discover(
            DiscoveryHandler<TScope>               handler,
            IOptions<SchemataAuthorizationOptions> options,
            HttpContext                            http,
            bool                                   oidc,
            CancellationToken                      ct
        ) {
            using var ambient = AdviceContext.Establish(new(http.RequestServices));
            var result = await handler.GetDiscoveryDocumentAsync(options.Value.Issuer!, oidc, ct);
            return Results.Json(result.Data);
        }

        var oidc = (DiscoveryHandler<TScope> handler, IOptions<SchemataAuthorizationOptions> options,
                    HttpContext http, CancellationToken ct) => Discover(handler, options, http, true, ct);
        var oauth = (DiscoveryHandler<TScope> handler, IOptions<SchemataAuthorizationOptions> options,
                     HttpContext http, CancellationToken ct) => Discover(handler, options, http, false, ct);

        var jwks = async (JwksHandler handler, CancellationToken ct) => {
            var result = await handler.ExecuteAsync(ct);
            return Results.Json(result.Data);
        };

        // The OIDC metadata profile requires an authorize endpoint; the OAuth profile and JWKS
        // stand alone. Handler registration is the same installation fact the MVC convention
        // uses to keep the Authorize actions routable.
        if (app.ApplicationServices.GetRequiredService<IServiceProviderIsService>()
               .IsService(typeof(IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>))) {
            endpoints.MapGet(CanonicalIssuer.OpenIdConfiguration(issuer), oidc).AllowAnonymous();
        }
        endpoints.MapGet(CanonicalIssuer.OAuthAuthorizationServer(issuer), oauth).AllowAnonymous();
        endpoints.MapGet(CanonicalIssuer.Jwks(issuer), jwks).AllowAnonymous();
    }
}
