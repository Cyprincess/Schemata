using Schemata.Authorization.Foundation;
using Schemata.Authorization.Grpc.Features;
using Schemata.Authorization.Skeleton.Entities;

namespace Microsoft.AspNetCore.Builder;

public static class SchemataAuthorizationBuilderExtensions
{
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> MapGrpc<TApp, TAuth, TScope, TToken>(this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder)
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFeature<SchemataAuthorizationGrpcFeature<TApp, TAuth, TScope, TToken>>();
        return builder;
    }
}
