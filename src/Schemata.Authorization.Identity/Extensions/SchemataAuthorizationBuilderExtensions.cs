using Schemata.Authorization.Foundation;
using Schemata.Authorization.Identity.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataAuthorizationIdentityBuilderExtensions
{
    /// <summary>
    ///     Wires the Identity-backed <see cref="Schemata.Authorization.Skeleton.ISubjectProvider" /> into the
    ///     Authorization pipeline. The subject (<c>sub</c> claim) resolves to a canonical resource name
    ///     (e.g., <c>users/chino</c>).
    /// </summary>
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseIdentity<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken {
        builder.Schemata.AddFeature<SchemataAuthorizationIdentityFeature>();
        return builder;
    }
}
