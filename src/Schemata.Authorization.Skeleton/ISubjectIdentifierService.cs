using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Resolves the subject identifier for a user within an application context.
///     Public clients receive the raw user identifier; pairwise clients receive a
///     deterministic hash so the same user cannot be correlated across applications,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#SubjectIDTypes">
///         OpenID Connect Core 1.0 §8:
///         Subject Identifier Types
///     </seealso>
///     .
/// </summary>
public interface ISubjectIdentifierService
{
    string Resolve(string userId, SchemataApplication application);
}
