using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation;

/// <summary>
///     Configures Schemata Authorization services and flow features for the selected entity types.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TAuth">The authorization entity type.</typeparam>
/// <typeparam name="TScope">The scope entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
public sealed class SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken>
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
    where TScope : SchemataScope
    where TToken : SchemataToken
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
    public SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> AddFlowFeature<T>()
        where T : IAuthorizationFlowFeature, new() {
        Configurators.Set<List<IAuthorizationFlowFeature>>(features => { features.Add(new T()); });

        return this;
    }
}
