// ReSharper disable CheckNamespace

using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata;
using Schemata.Features;

namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    #region Developer Exception Page Feature

    public static SchemataBuilder UseDeveloperExceptionPage(this SchemataBuilder builder) {
        builder.Configure((SchemataOptions options) => {
            options.AddFeature<SchemataDeveloperExceptionPageFeature>();
        });

        return builder;
    }

    #endregion

    #region HTTPS Feature

    public static SchemataBuilder UseHttps(this SchemataBuilder builder) {
        builder.Configure((SchemataOptions options) => { options.AddFeature<SchemataHttpsFeature>(); });

        return builder;
    }

    #endregion

    #region Static Files Feature

    public static SchemataBuilder UseStaticFiles(this SchemataBuilder builder) {
        builder.Configure((SchemataOptions options) => { options.AddFeature<SchemataStaticFilesFeature>(); });

        return builder;
    }

    #endregion

    #region Cookie Policy Feature

    public static SchemataBuilder UseCookiePolicy(
        this SchemataBuilder         builder,
        Action<CookiePolicyOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configurators.TryAdd(configure);

        builder.Configure((SchemataOptions options) => { options.AddFeature<SchemataCookiePolicyFeature>(); });

        return builder;
    }

    #endregion

    #region Routing Feature

    public static SchemataBuilder UseRouting(this SchemataBuilder builder) {
        builder.Configure((SchemataOptions options) => { options.AddFeature<SchemataRoutingFeature>(); });

        return builder;
    }

    #endregion

    #region CORS Feature

    public static SchemataBuilder UseCors(this SchemataBuilder builder, Action<CorsOptions>? configure = null) {
        configure ??= _ => { };
        builder.Configurators.TryAdd(configure);

        builder.Configure((SchemataOptions options) => { options.AddFeature<SchemataCorsFeature>(); });

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

        builder.Configure((SchemataOptions options) => { options.AddFeature<SchemataAuthenticationFeature>(); });

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

        builder.ConfigureServices(services => { services.TryAddTransient<ISessionStore, T>(); });

        builder.Configure((SchemataOptions options) => { options.AddFeature<SchemataSessionFeature>(); });

        return builder;
    }

    #endregion
}
