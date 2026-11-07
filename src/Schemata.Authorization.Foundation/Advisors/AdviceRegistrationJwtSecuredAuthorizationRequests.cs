using System.Collections.Generic;
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
) : IRegistrationRequestAdvisor<TApp>
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
                SchemataResources.REQUEST_OBJECT_SIGNING_ALG_NOT_SUPPORTED,
                new Dictionary<string, string?> { ["value"] = request.RequestObjectSigningAlg });
        }

        if (!string.IsNullOrWhiteSpace(request.RequestObjectEncryptionEnc)
            && string.IsNullOrWhiteSpace(request.RequestObjectEncryptionAlg)) {
            throw InvalidEncryptionMetadata();
        }

        var encryptionEnc = request.RequestObjectEncryptionEnc;
        if (!string.IsNullOrWhiteSpace(request.RequestObjectEncryptionAlg)) {
            encryptionEnc ??= ContentEncryptionAlgorithms.Aes128CbcHmacSha256;
            if (!options.Value.EncryptionAlgorithms.Contains(request.RequestObjectEncryptionAlg)
                || !options.Value.ContentEncryptionAlgorithms.Contains(encryptionEnc)) {
                throw InvalidEncryptionMetadata();
            }
        }

        return Task.FromResult(AdviseResult.Continue);
    }


    private static OAuthException InvalidEncryptionMetadata() {
        return new(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
    }
}
