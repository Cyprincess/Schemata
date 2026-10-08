using Schemata.Authorization.Foundation;
using Schemata.Authorization.Grpc.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataAuthorizationBuilderExtensions
{
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> MapGrpc<TApp, TAuth, TScope>(this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder)
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFeature<SchemataAuthorizationGrpcFeature<TApp, TAuth, TScope>>();
        return builder;
    }
}
