using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Core;
using Schemata.Core.Features;
#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.RateLimiting;
#endif

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    #region Logging Feature

    public static SchemataBuilder UseLogging(this SchemataBuilder builder, Action<ILoggingBuilder>? configure = null) {
        configure ??= logging => {
            // Add console logger by default
            logging.AddConsole();
        };

        builder.Configure(configure);

        builder.ReplaceLoggerFactory(LoggerFactory.Create(configure));

        builder.AddFeature<SchemataLoggingFeature>();

        return builder;
    }

    #endregion

    #region HTTP Logging Feature

    public static SchemataBuilder UseHttpLogging(
        this SchemataBuilder        builder,
        Action<HttpLoggingOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataHttpLoggingFeature>();

        return builder;
    }

    #endregion

    #region W3C Logging Feature

    public static SchemataBuilder UseW3CLogging(
        this SchemataBuilder      builder,
        Action<W3CLoggerOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataW3CLoggingFeature>();

        return builder;
    }

    #endregion

    #region Developer Exception Page Feature

    public static SchemataBuilder UseDeveloperExceptionPage(this SchemataBuilder builder) {
        builder.AddFeature<SchemataDeveloperExceptionPageFeature>();

        return builder;
    }

    #endregion

    #region Forwarded Headers Feature

    public static SchemataBuilder UseForwardedHeaders(
        this SchemataBuilder             builder,
        Action<ForwardedHeadersOptions>? configure = null) {
        configure ??= options => {
            options.ForwardedHeaders = ForwardedHeaders.All;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        };
        builder.Configure(configure);

        builder.AddFeature<SchemataForwardedHeadersFeature>();

        return builder;
    }

    #endregion

    #region HTTPS Feature

    public static SchemataBuilder UseHttps(this SchemataBuilder builder) {
        builder.AddFeature<SchemataHttpsFeature>();

        return builder;
    }

    #endregion

    #region Cookie Policy Feature

    public static SchemataBuilder UseCookiePolicy(
        this SchemataBuilder         builder,
        Action<CookiePolicyOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataCookiePolicyFeature>();

        return builder;
    }

    #endregion

    #region Routing Feature

    public static SchemataBuilder UseRouting(this SchemataBuilder builder) {
        builder.AddFeature<SchemataRoutingFeature>();

        return builder;
    }

    #endregion

    #region Quota Feature

#if NET8_0_OR_GREATER
    public static SchemataBuilder UseQuota(this SchemataBuilder builder, Action<RateLimiterOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataQuotaFeature>();

        return builder;
    }

#endif

    #endregion

    #region CORS Feature

    public static SchemataBuilder UseCors(this SchemataBuilder builder, Action<CorsOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataCorsFeature>();

        return builder;
    }

    #endregion

    #region Authentication Feature

    public static SchemataBuilder UseAuthentication(this SchemataBuilder builder, Action<AuthenticationBuilder> build) {
        return UseAuthentication(builder, build, null, null);
    }

    public static SchemataBuilder UseAuthentication(
        this SchemataBuilder          builder,
        Action<AuthenticationOptions> authenticate) {
        return UseAuthentication(builder, null, authenticate, null);
    }

    public static SchemataBuilder UseAuthentication(
        this SchemataBuilder         builder,
        Action<AuthorizationOptions> authorize) {
        return UseAuthentication(builder, null, null, authorize);
    }

    public static SchemataBuilder UseAuthentication(
        this SchemataBuilder          builder,
        Action<AuthenticationBuilder> build,
        Action<AuthorizationOptions>  authorize) {
        return UseAuthentication(builder, build, null, authorize);
    }

    public static SchemataBuilder UseAuthentication(
        this SchemataBuilder           builder,
        Action<AuthenticationBuilder>? build,
        Action<AuthenticationOptions>? authenticate,
        Action<AuthorizationOptions>?  authorize) {
        build ??= _ => { };
        builder.Configure(build);

        authenticate ??= _ => { };
        builder.Configure(authenticate);

        authorize ??= _ => { };
        builder.Configure(authorize);

        builder.AddFeature<SchemataAuthenticationFeature>();

        return builder;
    }

    #endregion

    #region Session Feature

    public static SchemataBuilder UseSession(this SchemataBuilder builder, Action<SessionOptions>? configure = null) {
        return UseSession<DistributedSessionStore>(builder, configure);
    }

    public static SchemataBuilder UseSession<T>(this SchemataBuilder builder, Action<SessionOptions>? configure = null)
        where T : class, ISessionStore {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataSessionFeature<T>>();

        return builder;
    }

    #endregion

    #region Controllers Feature

    public static SchemataBuilder UseControllers(
        this SchemataBuilder builder,
        Action<MvcOptions>?  configure = null,
        Action<IMvcBuilder>? build     = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        build ??= _ => { };
        builder.Configure(build);

        builder.AddFeature<SchemataControllersFeature>();

        return builder;
    }

    #endregion

    #region Json Serializer Feature

    public static SchemataBuilder UseJsonSerializer(this SchemataBuilder builder) {
        builder.AddFeature<SchemataJsonSerializerFeature>();

        return builder;
    }

    #endregion
}
