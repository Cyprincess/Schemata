using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors for the token revocation endpoint pipeline,
///     per <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html">RFC 7009: OAuth 2.0 Token Revocation</seealso>.
/// </summary>
public interface IRevocationAdvisor<TApplication, TToken> : IAdvisor<TApplication, RevokeRequest, TToken>
    where TApplication : SchemataApplication
    where TToken : SchemataToken;
