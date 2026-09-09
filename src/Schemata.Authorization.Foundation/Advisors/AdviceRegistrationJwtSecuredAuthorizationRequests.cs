using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

internal sealed class AdviceRegistrationJwtSecuredAuthorizationRequests<TApp>(
    IOptions<JwtSecuredAuthorizationRequestsOptions> options
) : IRegistrationRequestAdvisor<TApp>, IRegistrationResponseAdvisor<TApp>
    where TApp : SchemataApplication
{
    public int Order => SchemataConstants.Orders.Extension;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        RegisterRequest   request,
        TApp              application,
        CancellationToken ct = default
    ) {
        if (!string.IsNullOrWhiteSpace(request.RequestObjectSigningAlg)
            && !options.Value.SigningAlgorithms.Contains(request.RequestObjectSigningAlg)) {
            throw new OAuthException(
                OAuthErrors.InvalidClientMetadata,
                $"request_object_signing_alg {request.RequestObjectSigningAlg} is not supported by the authorization server.");
        }

        application.RequestObjectSigningAlg   = request.RequestObjectSigningAlg;
        application.RequireSignedRequestObject = request.RequireSignedRequestObject;
        return Task.FromResult(AdviseResult.Continue);
    }

    public Task<AdviseResult> AdviseAsync(
        AdviceContext         ctx,
        TApp                  application,
        RegistrationResponse response,
        CancellationToken     ct = default
    ) {
        response.RequestObjectSigningAlg   = application.RequestObjectSigningAlg;
        response.RequireSignedRequestObject = application.RequireSignedRequestObject;
        return Task.FromResult(AdviseResult.Continue);
    }
}
