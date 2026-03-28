using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDiscoveryCodeFlow(IOptions<CodeFlowOptions> codeOptions) : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryUserInfo.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

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
