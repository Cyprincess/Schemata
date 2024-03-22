// ReSharper disable CheckNamespace

using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.RateLimiting;
#endif

namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    #region Developer Exception Page Feature

    public static SchemataBuilder UseDeveloperExceptionPage(this SchemataBuilder builder) {
        builder.Options.AddFeature<SchemataDeveloperExceptionPageFeature>();

        return builder;
    }

    #endregion

    #region HTTPS Feature

    public static SchemataBuilder UseHttps(this SchemataBuilder builder) {
        builder.Options.AddFeature<SchemataHttpsFeature>();

        return builder;
    }

    #endregion

    #region Static Files Feature

    public static SchemataBuilder UseStaticFiles(this SchemataBuilder builder) {
        builder.Options.AddFeature<SchemataStaticFilesFeature>();

        return builder;
    }

    #endregion

    #region Cookie Policy Feature

    public static SchemataBuilder UseCookiePolicy(
        this SchemataBuilder         builder,
        Action<CookiePolicyOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configurators.TryAdd(configure);

        builder.Options.AddFeature<SchemataCookiePolicyFeature>();

        return builder;
    }

    #endregion

    #region Routing Feature

    public static SchemataBuilder UseRouting(this SchemataBuilder builder) {
        builder.Options.AddFeature<SchemataRoutingFeature>();

        return builder;
    }

    #endregion

    #region Routing Feature

#if NET8_0_OR_GREATER
    public static SchemataBuilder UseQuota(this SchemataBuilder builder, Action<RateLimiterOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configurators.TryAdd(configure);

        builder.Options.AddFeature<SchemataQuotaFeature>();

        return builder;
    }

#endif

    #endregion

    #region CORS Feature

    public static SchemataBuilder UseCors(this SchemataBuilder builder, Action<CorsOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configurators.TryAdd(configure);

        builder.Options.AddFeature<SchemataCorsFeature>();

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
        builder.Configurators.TryAdd(build);

        authenticate ??= _ => { };
        builder.Configurators.TryAdd(authenticate);

        authorize ??= _ => { };
        builder.Configurators.TryAdd(authorize);

        builder.Options.AddFeature<SchemataAuthenticationFeature>();

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
        builder.Configurators.TryAdd(configure);

        builder.Options.AddFeature<SchemataSessionFeature>();

        builder.ConfigureServices(services => { services.TryAddTransient<ISessionStore, T>(); });

        return builder;
    }

    #endregion

    #region Controller Feature

    public static SchemataBuilder UseControllers(this SchemataBuilder builder, Action<MvcOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configurators.TryAdd(configure);

        builder.Options.AddFeature<SchemataControllersFeature>();

        return builder;
    }

    #endregion
}
