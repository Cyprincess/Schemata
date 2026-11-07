using Schemata.Authorization.Foundation;
using Schemata.Authorization.Scim.Features;
using Schemata.Authorization.Skeleton.Entities;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Configures SCIM provisioning for the host-selected Application entity type.</summary>
public static class SchemataAuthorizationBuilderExtensions
{
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> UseScim<TApp, TAuth, TScope, TSubjectMapping>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> builder
    )
        where TApp : SchemataApplication, new()
        where TAuth : SchemataAuthorization, new()
        where TScope : SchemataScope
        where TSubjectMapping : SchemataSubjectMapping, new() {
        builder.AddFeature<SchemataAuthorizationScimFeature<TApp>>();
        return builder;
    }
}
