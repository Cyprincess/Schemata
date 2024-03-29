using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataAuthorizationBuilder UseAuthorization(
        this SchemataBuilder                       builder,
        Action<OpenIddictServerBuilder>?           serve     = null,
        Action<OpenIddictServerAspNetCoreBuilder>? integrate = null) {
        serve ??= _ => { };
        builder.Configure(serve);

        integrate ??= _ => { };
        builder.Configure(integrate);

        builder.AddFeature<SchemataAuthorizationFeature>();

        return new SchemataAuthorizationBuilder(builder);
    }
}
