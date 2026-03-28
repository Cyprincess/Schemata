using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Resolves the subject identifier for a user within an application context.
///     OIDC Core 1.0 §8: public returns userId as-is; pairwise returns a deterministic hash.
/// </summary>
public interface ISubjectIdentifierService
{
    string Resolve(string userId, SchemataApplication application);
}
