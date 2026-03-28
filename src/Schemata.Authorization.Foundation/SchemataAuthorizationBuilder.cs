using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation;

public sealed class SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken>
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
    where TScope : SchemataScope
    where TToken : SchemataToken
{
    internal SchemataAuthorizationBuilder(
        SchemataOptions    schemata,
        Configurators      configurators,
        IServiceCollection services
    ) {
        Schemata      = schemata;
        Configurators = configurators;
        Services      = services;
    }

    public SchemataOptions Schemata { get; }

    internal Configurators Configurators { get; }

    public IServiceCollection Services { get; }

    public SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> AddFlowFeature<T>()
        where T : IAuthorizationFlowFeature, new() {
        Configurators.Set<List<IAuthorizationFlowFeature>>(features => { features.Add(new T()); });

        return this;
    }
}
