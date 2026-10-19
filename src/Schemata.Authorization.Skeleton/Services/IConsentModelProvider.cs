using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Decides the <see cref="ConsentModel" /> an authorization request is evaluated under. The
///     model is host policy: the authorization pipeline resolves it per request and never reads
///     it from the persisted application.
/// </summary>
public interface IConsentModelProvider
{
    /// <summary>Resolves the consent model for <paramref name="application" /> and <paramref name="request" />.</summary>
    ConsentModel Resolve(SchemataApplication? application, AuthorizeRequest? request);
}
