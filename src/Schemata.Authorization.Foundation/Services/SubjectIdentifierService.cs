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

// OIDC Core 1.0 §8: public returns userId as-is; pairwise returns deterministic SHA-256 hash.
public class SubjectIdentifierService(IOptions<SchemataAuthorizationOptions> options) : ISubjectIdentifierService
{
    private readonly SchemataAuthorizationOptions _options = options.Value;

    #region ISubjectIdentifierService Members

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
