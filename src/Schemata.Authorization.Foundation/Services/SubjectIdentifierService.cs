using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     OIDC Subject Identifier Service per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#SubjectIDTypes">
///         OpenID Connect Core 1.0 §8: Subject
///         Identifier Types
///     </seealso>
///     .
///     When the subject type is <c>public</c>, returns the internal user ID
///     unchanged.  When <c>pairwise</c>, computes a deterministic SHA-256 hash
///     over <c>{sector_host} + {userId} + {pairwise_salt}</c> and returns the
///     Base64URL-encoded result.  The sector identifier is derived from the
///     application's <c>sector_identifier_uri</c>, falling back to the host
///     of the first registered redirect URI.
/// </summary>
public class SubjectIdentifierService(IOptions<SchemataAuthorizationOptions> options) : ISubjectIdentifierService
{
    private readonly SchemataAuthorizationOptions _options = options.Value;

    #region ISubjectIdentifierService Members

    /// <summary>
    ///     Resolves the subject identifier for the given user and application.
    /// </summary>
    /// <param name="userId">The internal (OP-local) user identifier.</param>
    /// <param name="application">The relying party application.</param>
    /// <returns>
    ///     The public or pairwise subject identifier suitable for use in
    ///     ID tokens and UserInfo responses.
    /// </returns>
    public string Resolve(string userId, SchemataApplication application) {
        var type = application.SubjectType ?? _options.SubjectType;

        if (type != SubjectTypes.Pairwise) {
            return userId;
        }

        var sector = GetSector(application);
        var salt   = _options.PairwiseSalt;
        var hash   = SHA256.HashData(Encoding.UTF8.GetBytes(sector + userId + salt));

        return Base64UrlEncoder.Encode(hash);
    }

    #endregion

    /// <summary>
    ///     Derives the sector identifier host per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#PairwiseAlg">
    ///         OpenID Connect Core 1.0 §8.1:
    ///         Pairwise Identifier Algorithm
    ///     </seealso>
    ///     :
    ///     uses <c>sector_identifier_uri</c> when configured, otherwise falls
    ///     back to the host of the first registered redirect URI.
    /// </summary>
    private static string GetSector(SchemataApplication application) {
        if (!string.IsNullOrWhiteSpace(application.SectorIdentifierUri)) {
            return new Uri(application.SectorIdentifierUri).Host;
        }

        var redirect = application.RedirectUris?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(redirect)) {
            throw new InvalidOperationException(SchemataResources.GetResourceString(SchemataResources.ST1017));
        }

        return new Uri(redirect).Host;
    }
}
