using System;
using Schemata.Core;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseSecurity(
        this SchemataBuilder             builder,
        Action<SchemataSecurityOptions>? configure = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataSecurityFeature>();

        return builder;
    }
}
