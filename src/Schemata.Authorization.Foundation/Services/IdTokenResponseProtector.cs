using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Creates Nested JWT ID tokens using the registered client's public key.</summary>
public sealed class IdTokenResponseProtector<TSecurity>(
    ClientEncryptionKeyResolver<TSecurity> resolver
) : IIdTokenResponseProtector
    where TSecurity : Schemata.Security.Skeleton.Entities.SchemataSecurity, new()
{
    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<string> ProtectAsync(
        SchemataApplication application,
        string              signedToken,
        CancellationToken   ct = default
    ) {
        if (string.IsNullOrWhiteSpace(application.IdTokenEncryptedResponseAlg)) {
            if (!string.IsNullOrWhiteSpace(application.IdTokenEncryptedResponseEnc)) {
                throw new InvalidOperationException("id_token_encrypted_response_enc requires id_token_encrypted_response_alg.");
            }
            return signedToken;
        }

        var credentials = await resolver.ResolveAsync(
            application, application.IdTokenEncryptedResponseAlg, application.IdTokenEncryptedResponseEnc, ct);
        return Handler.EncryptToken(signedToken, credentials);
    }
}
