using Schemata.Authorization.Foundation;
using Schemata.Authorization.Http.Features;
using Schemata.Authorization.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataAuthorizationBuilderExtensions
{
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> MapHttp<TApp, TAuth, TScope, TSubjectMapping>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> builder
    )
        where TApp : SchemataApplication, new()
        where TAuth : SchemataAuthorization, new()
        where TScope : SchemataScope
        where TSubjectMapping : SchemataSubjectMapping, new() {
        builder.AddFeature<SchemataAuthorizationHttpFeature>();
        return builder;
    }
}
