using System;
using System.Linq;
using System.Reflection;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods on <see cref="SchemataBuilder" /> for registering the Schemata Authorization feature,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
///     and <seealso href="https://openid.net/specs/openid-connect-core-1_0.html">OpenID Connect Core 1.0</seealso>.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds the Schemata Authorization server with default entity types to the application.
    /// </summary>
    /// <param name="builder">The Schemata host builder.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="SchemataAuthorizationOptions" />.</param>
    /// <returns>
    ///     A <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TSubjectMapping}" /> for chaining flow
    ///     feature extensions.
    /// </returns>
    /// <remarks>
    ///     Installs <see cref="SchemataAuthorizationFeature{TApp,TAuth,TScope,TSubjectMapping,TSecurity,TToken}" /> as the core
    ///     feature.
    /// </remarks>
    public static SchemataAuthorizationBuilder<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataSubjectMapping> UseAuthorization(
        this SchemataBuilder                  builder,
        Action<SchemataAuthorizationOptions>? configure = null
    ) {
        return builder.UseAuthorization<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataSubjectMapping>(configure);
    }

    /// <summary>
    ///     Adds the Schemata Authorization server with custom entity types to the application,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
    ///     and <seealso href="https://openid.net/specs/openid-connect-core-1_0.html">OpenID Connect Core 1.0</seealso>.
    /// </summary>
    /// <typeparam name="TApplication">The application entity type.</typeparam>
    /// <typeparam name="TAuthorization">The authorization entity type.</typeparam>
    /// <typeparam name="TScope">The scope entity type.</typeparam>
    /// <typeparam name="TSubjectMapping">The pairwise subject-mapping entity type.</typeparam>
    /// <param name="builder">The Schemata host builder.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="SchemataAuthorizationOptions" />.</param>
    /// <returns>
    ///     A <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TSubjectMapping}" /> for chaining flow
    ///     feature extensions.
    /// </returns>
    /// <remarks>
    ///     The installed feature maps the OIDC/OAuth authorization-server metadata and JWKS endpoints at
    ///     the paths the configured issuer derives.
    ///     The token entity type is the one selected by the registered security feature, so call
    ///     <c>UseSecurity()</c> first.
    ///     Installs <see cref="SchemataAuthorizationFeature{TApp,TAuth,TScope,TSubjectMapping,TSecurity,TToken}" /> as the core
    ///     feature.
    /// </remarks>
    public static SchemataAuthorizationBuilder<TApplication, TAuthorization, TScope, TSubjectMapping>
        UseAuthorization<TApplication, TAuthorization, TScope, TSubjectMapping>(
            this SchemataBuilder                  builder,
            Action<SchemataAuthorizationOptions>? configure = null
        )
        where TApplication : SchemataApplication, new()
        where TAuthorization : SchemataAuthorization, new()
        where TScope : SchemataScope
        where TSubjectMapping : SchemataSubjectMapping, new() {
        var providers = builder.Options.GetFeatures()?.Values.OfType<ISecurityEntityProvider>().ToArray() ?? [];
        if (providers.Length == 0) {
            throw new InvalidOperationException(
                "UseAuthorization requires a registered security feature; call UseSecurity() before UseAuthorization().");
        }

        if (providers.Length > 1) {
            throw new InvalidOperationException(
                $"UseAuthorization requires exactly one registered security feature but found {providers.Length}; register one UseSecurity() call.");
        }

        return Closed.Use<TApplication, TAuthorization, TScope, TSubjectMapping>(
            builder, configure, providers[0].SecurityEntityType, providers[0].TokenEntityType);
    }

    private static class Closed
    {
        private static readonly MethodInfo UseMethod = typeof(Closed).GetMethod(
            nameof(UseAuthorizationCore), BindingFlags.NonPublic | BindingFlags.Static)!;

        public static SchemataAuthorizationBuilder<TApplication, TAuthorization, TScope, TSubjectMapping> Use<
            TApplication, TAuthorization, TScope, TSubjectMapping>(
            SchemataBuilder                       builder,
            Action<SchemataAuthorizationOptions>? configure,
            Type                                  security,
            Type                                  token
        )
            where TApplication : SchemataApplication, new()
            where TAuthorization : SchemataAuthorization, new()
            where TScope : SchemataScope
            where TSubjectMapping : SchemataSubjectMapping, new() {
            return (SchemataAuthorizationBuilder<TApplication, TAuthorization, TScope, TSubjectMapping>)UseMethod
               .MakeGenericMethod(typeof(TApplication), typeof(TAuthorization), typeof(TScope), typeof(TSubjectMapping),
                   security, token)
               .Invoke(null, [builder, configure])!;
        }

        private static SchemataAuthorizationBuilder<TApplication, TAuthorization, TScope, TSubjectMapping> UseAuthorizationCore<
            TApplication, TAuthorization, TScope, TSubjectMapping, TSecurity, TToken>(
            SchemataBuilder                       builder,
            Action<SchemataAuthorizationOptions>? configure
        )
            where TApplication : SchemataApplication, new()
            where TAuthorization : SchemataAuthorization, new()
            where TScope : SchemataScope
            where TSubjectMapping : SchemataSubjectMapping, new()
            where TSecurity : SchemataSecurity, new()
            where TToken : SchemataToken, new() {
            configure ??= _ => { };
            builder.Configure(configure);

            builder.AddFeature<SchemataAuthorizationFeature<TApplication, TAuthorization, TScope, TSubjectMapping, TSecurity, TToken>>();

            return new SchemataAuthorizationBuilder<TApplication, TAuthorization, TScope, TSubjectMapping, TSecurity, TToken>(
                builder.Options, builder.Configurators, builder.Services);
        }
    }
}
