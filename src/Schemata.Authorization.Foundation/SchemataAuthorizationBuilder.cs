using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Core.Features;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Foundation;

/// <summary>
///     Constants shared by the authorization builder and the components that extend it.
/// </summary>
public static class SchemataAuthorizationBuilder
{
    /// <summary>Options key carrying the authentication scheme selected for authorization resources.</summary>
    public const string AuthenticationSchemeKey = "Authorization:AuthenticationScheme";

    /// <summary>Options key carrying the host entity set exposed to transport resource features.</summary>
    public const string ResourceRegistrationKey = "Authorization:ResourceRegistration";
}

/// <summary>
///     Configures Schemata Authorization services and flow features for the selected application,
///     authorization, scope, and subject-mapping entity types. The token entity type comes from the
///     registered security feature, so it is not repeated here.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TAuth">The authorization entity type.</typeparam>
/// <typeparam name="TScope">The scope entity type.</typeparam>
/// <typeparam name="TSubjectMapping">The pairwise subject-mapping entity type.</typeparam>
public abstract class SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> : IAuthorizationBuilder
    where TApp : SchemataApplication, new()
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
    where TSubjectMapping : SchemataSubjectMapping, new()
{
    /// <summary>
    ///     Creates an authorization builder over the host options, configurator store, and service collection.
    /// </summary>
    /// <param name="schemata">The host Schemata options.</param>
    /// <param name="configurators">The configurator store used by feature registration.</param>
    /// <param name="services">The service collection being configured.</param>
    internal SchemataAuthorizationBuilder(
        SchemataOptions    schemata,
        Configurators      configurators,
        IServiceCollection services
    ) {
        Schemata      = schemata;
        Configurators = configurators;
        Services      = services;
        var registrations = Schemata.Get<Dictionary<IResourceBuilder, ResourceSecurityRegistration>>(nameof(ResourceSecurityRegistration)) ?? new();
        Schemata.Set(nameof(ResourceSecurityRegistration), registrations);
        registrations[this] = new(
            services => new SchemataResourceBuilder(Schemata, services).WithAuthentication(),
            services => new SchemataResourceBuilder(Schemata, services).WithAuthorization(),
            scheme => Schemata.Set(SchemataAuthorizationBuilder.AuthenticationSchemeKey, scheme));
    }

    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }

    /// <summary>
    ///     Gets the host Schemata options used by authorization feature registration.
    /// </summary>
    public SchemataOptions Schemata { get; }

    /// <summary>
    ///     Gets the configurator store that collects authorization flow features.
    /// </summary>
    internal Configurators Configurators { get; }

    /// <summary>
    ///     Gets the service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Adds an authorization flow feature to the registration pipeline.
    /// </summary>
    /// <typeparam name="T">The flow feature type to instantiate and register.</typeparam>
    /// <returns>The current builder.</returns>
    public SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddFlowFeature<T>()
        where T : IAuthorizationFlowFeature, new() {
        Configurators.Set<List<IAuthorizationFlowFeature>>(features => { features.Add(new T()); });

        return this;
    }

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddAuthorizationCodeFlow(
        Action<CodeFlowOptions>? configure
    );

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddRefreshTokenFlow(
        Action<RefreshTokenFlowOptions>? configure
    );

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddDeviceFlow();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddJwtBearerGrant();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddPushedAuthorizationRequests(
        Action<PushedAuthorizationRequestsOptions>? configure
    );

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddNativeSingleSignOn(
        Action<NativeSingleSignOnOptions>? configure
    );

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddClientAssertionAuthentication();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddJwtSecuredAuthorizationRequests(
        Action<JwtSecuredAuthorizationRequestsOptions>? configure
    );

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddUserInfo();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddIntrospection();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddDynamicClientRegistration();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddRegistrationReplace();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddRegistrationDelete();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddRevocation();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddFrontChannelLogout();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddBackChannelLogout();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddEndSession();

    internal abstract SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddDemonstratingProofOfPossession(
        Action<DPopOptions>? configure
    );
}

/// <summary>
///     Closes the authorization builder over the token entity type published by the registered
///     security feature so token-bearing flow registrations stay type-checked.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TAuth">The authorization entity type.</typeparam>
/// <typeparam name="TScope">The scope entity type.</typeparam>
/// <typeparam name="TSubjectMapping">The pairwise subject-mapping entity type.</typeparam>
/// <typeparam name="TSecurity">The security entity type selected by the registered security feature.</typeparam>
/// <typeparam name="TToken">The token entity type selected by the registered security feature.</typeparam>
internal sealed class SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping, TSecurity, TToken>
    : SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping>
    where TApp : SchemataApplication, new()
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
    where TSubjectMapping : SchemataSubjectMapping, new()
    where TSecurity : SchemataSecurity, new()
    where TToken : SchemataToken, new()
{
    internal SchemataAuthorizationBuilder(
        SchemataOptions    schemata,
        Configurators      configurators,
        IServiceCollection services
    ) : base(schemata, configurators, services) { }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddAuthorizationCodeFlow(
        Action<CodeFlowOptions>? configure
    ) {
        if (configure is not null) {
            Configurators.Set(configure);
        }

        AddFlowFeature<TokenFeature>();
        AddFlowFeature<InteractionFeature>();
        AddFlowFeature<AuthorizationCodeFlowFeature<TApp, TAuth, TScope, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddRefreshTokenFlow(
        Action<RefreshTokenFlowOptions>? configure
    ) {
        if (configure is not null) {
            Configurators.Set(configure);
        }

        AddFlowFeature<TokenFeature>();
        AddFlowFeature<RefreshTokenFlowFeature<TApp, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddDeviceFlow() {
        AddFlowFeature<TokenFeature>();
        AddFlowFeature<InteractionFeature>();
        AddFlowFeature<DeviceFlowFeature<TApp, TAuth, TScope, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddJwtBearerGrant() {
        AddFlowFeature<TokenFeature>();
        AddFlowFeature<JwtBearerGrantFeature<TApp, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddPushedAuthorizationRequests(
        Action<PushedAuthorizationRequestsOptions>? configure
    ) {
        if (configure is not null) {
            Configurators.Set(configure);
        }

        AddFlowFeature<TokenFeature>();
        AddFlowFeature<PushedAuthorizationRequestsFeature<TApp, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddNativeSingleSignOn(
        Action<NativeSingleSignOnOptions>? configure
    ) {
        if (configure is not null) {
            Configurators.Set(configure);
        }

        AddFlowFeature<TokenFeature>();
        AddFlowFeature<TokenExchangeFeature<TApp>>();
        AddFlowFeature<NativeSingleSignOnFeature<TApp, TAuth, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddClientAssertionAuthentication() {
        AddFlowFeature<ClientAssertionAuthenticationFeature<TApp, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddJwtSecuredAuthorizationRequests(
        Action<JwtSecuredAuthorizationRequestsOptions>? configure
    ) {
        if (configure is not null) {
            Configurators.Set(configure);
        }

        AddFlowFeature<JwtSecuredAuthorizationRequestsFeature<TApp, TSecurity>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddUserInfo() {
        AddFlowFeature<UserInfoFeature<TApp, TSecurity>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddIntrospection() {
        AddFlowFeature<IntrospectionFeature<TApp, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddDynamicClientRegistration() {
        AddFlowFeature<DynamicRegistrationFeature<TApp, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddRegistrationReplace() {
        AddFlowFeature<RegistrationReplaceFeature<TApp, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddRegistrationDelete() {
        AddFlowFeature<RegistrationDeleteFeature<TApp, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddRevocation() {
        AddFlowFeature<RevocationFeature<TApp, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddFrontChannelLogout() {
        AddFlowFeature<FrontChannelLogoutFeature<TApp, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddBackChannelLogout() {
        AddFlowFeature<BackChannelLogoutFeature<TApp, TSubjectMapping, TSecurity, TToken>>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddEndSession() {
        AddFlowFeature<EndSessionFeature<TApp, TSecurity, TToken>>();
        AddFlowFeature<InteractionFeature>();

        return this;
    }

    internal override SchemataAuthorizationBuilder<TApp, TAuth, TScope, TSubjectMapping> AddDemonstratingProofOfPossession(
        Action<DPopOptions>? configure
    ) {
        if (configure is not null) {
            Configurators.Set(configure);
        }

        AddFlowFeature<DemonstratingProofOfPossessionFeature<TApp, TSecurity, TToken>>();

        return this;
    }
}
