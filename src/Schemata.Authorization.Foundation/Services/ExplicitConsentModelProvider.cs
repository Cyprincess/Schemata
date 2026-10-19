using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Default <see cref="IConsentModelProvider" />: every application is evaluated under
///     <see cref="ConsentModel.Explicit" />. Hosts relocate consent policy by registering their
///     own implementation.
/// </summary>
public sealed class ExplicitConsentModelProvider : IConsentModelProvider
{
    /// <inheritdoc />
    public ConsentModel Resolve(SchemataApplication? application, AuthorizeRequest? request) {
        return ConsentModel.Explicit;
    }
}
