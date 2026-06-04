using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the authorization code flow metadata to the discovery document: <c>authorization_endpoint</c>,
///     <c>authorization_code</c> grant, and PKCE methods,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderConfig">
///         OpenID Connect Discovery 1.0
///         §4: Obtaining OpenID Provider Configuration Information
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     Advertises PKCE methods (<c>S256</c> always, <c>plain</c> only when not forbidden by
///     <see cref="CodeFlowOptions.RequirePkceS256" />).
/// </remarks>
/// <seealso cref="CodeFlowOptions" />
public sealed class AdviceDiscoveryCodeFlow(IOptions<CodeFlowOptions> codeOptions) : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryUserInfo.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;
        discovery.Document                               ??= new();
        discovery.Document.GrantTypesSupported           ??= [];
        discovery.Document.CodeChallengeMethodsSupported ??= [];
        discovery.Document.AuthorizationEndpoint         =   $"{issuer}{Endpoints.Authorize}";
        discovery.Document.GrantTypesSupported.Add(GrantTypes.AuthorizationCode);
        discovery.Document.CodeChallengeMethodsSupported.Add(PkceMethods.S256);
        if (!codeOptions.Value.RequirePkceS256) {
            discovery.Document.CodeChallengeMethodsSupported.Add(PkceMethods.Plain);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
