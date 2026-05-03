using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for registering Schemata on
///     <see cref="WebApplicationBuilder" />. Delegates to
///     <c>ServiceCollectionExtensions.AddSchemata</c>.
/// </summary>
public static class WebApplicationBuilderExtensions
{
    /// <summary>
    ///     Bootstraps Schemata on <see cref="WebApplicationBuilder" /> with default
    ///     options.
    /// </summary>
    /// <param name="builder">The <see cref="WebApplicationBuilder" />.</param>
    /// <returns>The <see cref="WebApplicationBuilder" /> for chaining.</returns>
    public static WebApplicationBuilder UseSchemata(this WebApplicationBuilder builder) {
        return builder.UseSchemata(_ => { }, _ => { });
    }

    /// <summary>
    ///     Bootstraps Schemata on <see cref="WebApplicationBuilder" /> and
    ///     configures features via <paramref name="schema" />.
    /// </summary>
    /// <param name="builder">The <see cref="WebApplicationBuilder" />.</param>
    /// <param name="schema">Callback that configures features and services on the builder.</param>
    /// <returns>The <see cref="WebApplicationBuilder" /> for chaining.</returns>
    public static WebApplicationBuilder UseSchemata(
        this WebApplicationBuilder builder,
        Action<SchemataBuilder>?   schema
    ) {
        return builder.UseSchemata(schema, _ => { });
    }

    /// <summary>
    ///     Bootstraps Schemata on <see cref="WebApplicationBuilder" />,
    ///     configuring features via <paramref name="schema" /> and mutating
    ///     <see cref="SchemataOptions" /> via <paramref name="configure" />.
    /// </summary>
    /// <param name="builder">The <see cref="WebApplicationBuilder" />.</param>
    /// <param name="schema">Callback that configures features and services on the builder.</param>
    /// <param name="configure">Callback that mutates <see cref="SchemataOptions" /> directly.</param>
    /// <returns>The <see cref="WebApplicationBuilder" /> for chaining.</returns>
    public static WebApplicationBuilder UseSchemata(
        this WebApplicationBuilder builder,
        Action<SchemataBuilder>?   schema,
        Action<SchemataOptions>?   configure
    ) {
        builder.Services.AddSchemata(builder.Configuration, builder.Environment, schema, configure);

        return builder;
    }
}
