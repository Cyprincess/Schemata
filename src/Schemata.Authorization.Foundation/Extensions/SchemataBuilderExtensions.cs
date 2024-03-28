using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Features;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseAuthorization(
        this SchemataBuilder             builder,
        Action<OpenIddictServerBuilder>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.Options.AddFeature<SchemataAuthorizationFeature>();

        return builder;
    }
}
