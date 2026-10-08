using Schemata.Authorization.Foundation;
using Schemata.Authorization.Identity.Features;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for adding Identity-backed authorization services.
/// </summary>
public static class SchemataAuthorizationIdentityBuilderExtensions
{
    /// <summary>
    ///     Wires the Identity-backed <see cref="Schemata.Authorization.Skeleton.ISubjectProvider" /> into the
    ///     Authorization pipeline.
    /// </summary>
    public static IAuthorizationBuilder UseIdentity<TUser>(this IAuthorizationBuilder builder)
        where TUser : SchemataUser {
        builder.Schemata.AddFeature<SchemataAuthorizationIdentityFeature<TUser>>();
        return builder;
    }
}
