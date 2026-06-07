using System;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Core;
using Schemata.Core.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Fluent extension methods on <see cref="SchemataBuilder" />. Each method
///     registers a feature type and its corresponding deferred configurator in a
///     single call.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Enables <c>Logging</c>, which provides console logging by default and
    ///     replaces the logger factory so loggers created through
    ///     <see cref="SchemataOptions" /> respect the same configuration.
    ///     See <see cref="SchemataLoggingFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="ILoggingBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseLogging(this SchemataBuilder builder, Action<ILoggingBuilder>? configure = null) {
        configure ??= logging => {
            logging.AddConsole();
        };

        builder.Configure(configure);

        builder.ReplaceLoggerFactory(LoggerFactory.Create(configure));

        builder.AddFeature<SchemataLoggingFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>HttpLogging</c>, which provides HTTP request/response logging.
    ///     See <see cref="SchemataHttpLoggingFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="HttpLoggingOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseHttpLogging(
        this SchemataBuilder        builder,
        Action<HttpLoggingOptions>? configure = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataHttpLoggingFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>W3CLogging</c>, which provides W3C-format request logging.
    ///     See <see cref="SchemataW3CLoggingFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="W3CLoggerOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseW3CLogging(
        this SchemataBuilder      builder,
        Action<W3CLoggerOptions>? configure = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataW3CLoggingFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>DeveloperExceptionPage</c>, which shows detailed exception
    ///     pages in Development only.
    ///     See <see cref="SchemataDeveloperExceptionPageFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseDeveloperExceptionPage(this SchemataBuilder builder) {
        builder.AddFeature<SchemataDeveloperExceptionPageFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>ForwardedHeaders</c>, which respects <c>X-Forwarded-*</c>
    ///     headers from reverse proxies. See <seealso href="https://google.aip.dev/196">AIP-196: Regional endpoints</seealso>.
    ///     See <see cref="SchemataForwardedHeadersFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="ForwardedHeadersOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseForwardedHeaders(
        this SchemataBuilder             builder,
        Action<ForwardedHeadersOptions>? configure = null
    ) {
        configure ??= options => {
            options.ForwardedHeaders = ForwardedHeaders.All;
#if NET10_0_OR_GREATER
            options.KnownIPNetworks.Clear();
#else
            options.KnownNetworks.Clear();
#endif
            options.KnownProxies.Clear();
        };
        builder.Configure(configure);

        builder.AddFeature<SchemataForwardedHeadersFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>Https</c>, which enforces HSTS and HTTPS redirection in
    ///     non-Development environments.
    ///     See <see cref="SchemataHttpsFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseHttps(this SchemataBuilder builder) {
        builder.AddFeature<SchemataHttpsFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>CookiePolicy</c>, which applies cookie consent and security
    ///     policies.
    ///     See <see cref="SchemataCookiePolicyFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="CookiePolicyOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseCookiePolicy(
        this SchemataBuilder         builder,
        Action<CookiePolicyOptions>? configure = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataCookiePolicyFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>Routing</c>, which registers routing services and inserts
    ///     endpoint routing middleware.
    ///     See <see cref="SchemataRoutingFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseRouting(this SchemataBuilder builder) {
        builder.AddFeature<SchemataRoutingFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>WellKnown</c>, which serves <c>/.well-known/</c> routes.
    ///     See <see cref="SchemataWellKnownFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="WellKnownOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseWellKnown(
        this SchemataBuilder      builder,
        Action<WellKnownOptions>? configure = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataWellKnownFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>Quota</c>, which provides rate limiting with
    ///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>
    ///     compliant error responses.
    ///     See <see cref="SchemataQuotaFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="RateLimiterOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseQuota(this SchemataBuilder builder, Action<RateLimiterOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataQuotaFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>Cors</c>, which configures cross-origin request handling.
    ///     See <see cref="SchemataCorsFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="CorsOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseCors(this SchemataBuilder builder, Action<CorsOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataCorsFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>Controllers</c>, which registers MVC controllers with
    ///     endpoint routing. Strips <c>Schemata.*</c> assemblies from the
    ///     ApplicationPartManager to avoid duplicate controller discovery.
    ///     See <see cref="SchemataControllersFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="MvcOptions" />.</param>
    /// <param name="build">Optional configuration delegate for <see cref="IMvcBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseControllers(
        this SchemataBuilder builder,
        Action<MvcOptions>?  configure = null,
        Action<IMvcBuilder>? build     = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);

        build ??= _ => { };
        builder.Configure(build);

        builder.AddFeature<SchemataControllersFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>JsonSerializer</c>, which configures
    ///     <see cref="JsonSerializerOptions" /> with snake_case naming,
    ///     string-number coercion, kebab-case enums, and polymorphic type
    ///     resolution.
    ///     See <see cref="SchemataJsonSerializerFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="JsonSerializerOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseJsonSerializer(
        this SchemataBuilder           builder,
        Action<JsonSerializerOptions>? configure = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataJsonSerializerFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>Authentication</c> with an
    ///     <see cref="AuthenticationBuilder" /> callback.
    ///     See <see cref="SchemataAuthenticationFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="build">A delegate to configure the <see cref="AuthenticationBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseAuthentication(this SchemataBuilder builder, Action<AuthenticationBuilder> build) {
        return builder.UseAuthentication(build, null, null);
    }

    /// <summary>
    ///     Enables <c>Authentication</c> with an
    ///     <see cref="AuthenticationOptions" /> callback.
    ///     See <see cref="SchemataAuthenticationFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="authenticate">A delegate to configure <see cref="AuthenticationOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseAuthentication(
        this SchemataBuilder          builder,
        Action<AuthenticationOptions> authenticate
    ) {
        return builder.UseAuthentication(null, authenticate, null);
    }

    /// <summary>
    ///     Enables <c>Authentication</c> with an
    ///     <see cref="AuthorizationOptions" /> callback.
    ///     See <see cref="SchemataAuthenticationFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="authorize">A delegate to configure <see cref="AuthorizationOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseAuthentication(
        this SchemataBuilder         builder,
        Action<AuthorizationOptions> authorize
    ) {
        return builder.UseAuthentication(null, null, authorize);
    }

    /// <summary>
    ///     Enables <c>Authentication</c> with both
    ///     <see cref="AuthenticationBuilder" /> and
    ///     <see cref="AuthorizationOptions" /> callbacks.
    ///     See <see cref="SchemataAuthenticationFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="build">A delegate to configure the <see cref="AuthenticationBuilder" />.</param>
    /// <param name="authorize">A delegate to configure <see cref="AuthorizationOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseAuthentication(
        this SchemataBuilder          builder,
        Action<AuthenticationBuilder> build,
        Action<AuthorizationOptions>  authorize
    ) {
        return builder.UseAuthentication(build, null, authorize);
    }

    /// <summary>
    ///     Enables <c>Authentication</c>, which provides authentication and
    ///     authorization services with callbacks for
    ///     <see cref="AuthenticationBuilder" />,
    ///     <see cref="AuthenticationOptions" />, and
    ///     <see cref="AuthorizationOptions" />.
    ///     See <see cref="SchemataAuthenticationFeature" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="build">Optional configuration delegate for <see cref="AuthenticationBuilder" />.</param>
    /// <param name="authenticate">Optional configuration delegate for <see cref="AuthenticationOptions" />.</param>
    /// <param name="authorize">Optional configuration delegate for <see cref="AuthorizationOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseAuthentication(
        this SchemataBuilder           builder,
        Action<AuthenticationBuilder>? build,
        Action<AuthenticationOptions>? authenticate,
        Action<AuthorizationOptions>?  authorize
    ) {
        build ??= _ => { };
        builder.Configure(build);

        authenticate ??= _ => { };
        builder.Configure(authenticate);

        authorize ??= _ => { };
        builder.Configure(authorize);

        builder.AddFeature<SchemataAuthenticationFeature>();

        return builder;
    }

    /// <summary>
    ///     Enables <c>Session</c> with the default
    ///     <see cref="DistributedSessionStore" />.
    ///     See <see cref="SchemataSessionFeature{T}" /> for details.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="SessionOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseSession(this SchemataBuilder builder, Action<SessionOptions>? configure = null) {
        return builder.UseSession<DistributedSessionStore>(configure);
    }

    /// <summary>
    ///     Enables <c>Session</c> with the specified <typeparamref name="T" />
    ///     store. Depends on <see cref="SchemataCookiePolicyFeature" />.
    ///     See <see cref="SchemataSessionFeature{T}" /> for details.
    /// </summary>
    /// <typeparam name="T">A concrete <see cref="ISessionStore" /> implementation.</typeparam>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="SessionOptions" />.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseSession<T>(this SchemataBuilder builder, Action<SessionOptions>? configure = null)
        where T : class, ISessionStore {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataSessionFeature<T>>();

        return builder;
    }
}
