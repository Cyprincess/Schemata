using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Core.Features;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Flow.Foundation.Builders;

/// <summary>
///     Fluent builder for configuring the flow system. Process definitions registered through
///     <c>Use&lt;TProcess&gt;</c> are written directly to <see cref="SchemataFlowOptions" />, and the
///     seven entity type parameters select the persisted rows the runtime closes on.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
/// <typeparam name="TCompensation">The persisted compensation entity type registered by the host.</typeparam>
/// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
/// <typeparam name="TSource">The persisted source-binding entity type registered by the host.</typeparam>
/// <typeparam name="TEffectIntent">The persisted effect-intent entity type registered by the host.</typeparam>
public sealed class SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> : IResourceBuilder
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
    where TEffectIntent : SchemataFlowEffectIntent, new()
{
    /// <summary>Initializes the builder bound to the given options and service collection.</summary>
    /// <param name="schemata">Receives the feature registrations added through <see cref="AddFeature{T}" />.</param>
    /// <param name="services">The service collection that receives process registrations.</param>
    public SchemataFlowBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
        var registrations = Schemata.Get<Dictionary<IResourceBuilder, ResourceSecurityRegistration>>(nameof(ResourceSecurityRegistration)) ?? new();
        Schemata.Set(nameof(ResourceSecurityRegistration), registrations);
        registrations[this] = new(
            services => FlowAuthorizationRegistration<TProcess, TToken, TTransition, TParticipant>.AddFlowAuthentication(services),
            services => {
                FlowAuthorizationRegistration<TProcess, TToken, TTransition, TParticipant>.AddFlowAuthorization(services);
                new SchemataResourceBuilder(Schemata, services)
                    .WithAuthorization<TProcess>()
                    .WithAuthorization<TToken>()
                    .WithAuthorization<TTransition>();
            },
            scheme => Schemata.Set(FlowResourceRegistration<TProcess, TToken>.AuthenticationSchemeKey, scheme));
    }

    public SchemataOptions Schemata { get; }

    /// <summary>Service collection that receives process registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Adds a feature to the Schemata configuration.
    /// </summary>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }


    /// <summary>
    ///     Registers a code-first process definition type, writing its
    ///     <see cref="ProcessConfiguration" /> to <see cref="SchemataFlowOptions" />. Returns this
    ///     builder so multiple definitions chain; configure the definition through
    ///     <paramref name="configure" />.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> Use<TDefinition>(Action<ProcessConfiguration>? configure = null)
        where TDefinition : ProcessDefinition {
        return Use<TDefinition>(null, configure);
    }

    /// <summary>
    ///     Registers a code-first process definition type against a specific engine, writing its
    ///     <see cref="ProcessConfiguration" /> to <see cref="SchemataFlowOptions" />.
    /// </summary>
    /// <param name="engine">The engine name; <see langword="null" /> uses the state-machine engine.</param>
    /// <param name="configure">Mutates the prepared <see cref="ProcessConfiguration" /> after its defaults are set and before it is added to the options.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> Use<TDefinition>(string? engine, Action<ProcessConfiguration>? configure = null)
        where TDefinition : ProcessDefinition {
        var config = new ProcessConfiguration {
            Name           = typeof(TDefinition).Name,
            Engine         = engine ?? FlowConstants.Engines.StateMachine,
            DefinitionType = typeof(TDefinition),
        };

        configure?.Invoke(config);

        Services.Configure<SchemataFlowOptions>(options => options.Configurations.Add(config));

        return this;
    }
}
