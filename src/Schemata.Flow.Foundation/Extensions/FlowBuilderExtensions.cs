using System;
using Schemata.Core;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>UseFlow</c> extension method on <see cref="SchemataBuilder" />.
/// </summary>
public static class FlowBuilderExtensions
{
    /// <summary>
    ///     Enables the <see cref="SchemataFlowFeature" /> for the default persisted Flow entity
    ///     types and returns a builder for registering process definitions.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The flow builder for chaining.</returns>
    public static SchemataFlowBuilder<SchemataProcess, SchemataProcessToken, SchemataProcessTransition, SchemataProcessCompensation, SchemataProcessParticipant, SchemataProcessSource, SchemataFlowEffectIntent> UseFlow(this SchemataBuilder builder) {
        return builder.UseFlow<
            SchemataProcess, SchemataProcessToken, SchemataProcessTransition,
            SchemataProcessCompensation, SchemataProcessParticipant, SchemataProcessSource,
            SchemataFlowEffectIntent>();
    }

    /// <summary>
    ///     Enables Flow closed on the host-selected persisted entity types and returns a builder for
    ///     registering process definitions.
    /// </summary>
    /// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
    /// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
    /// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
    /// <typeparam name="TCompensation">The persisted compensation entity type registered by the host.</typeparam>
    /// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
    /// <typeparam name="TSource">The persisted source-binding entity type registered by the host.</typeparam>
    /// <typeparam name="TEffectIntent">The persisted effect-intent entity type registered by the host.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The flow builder for chaining.</returns>
    /// <seealso cref="SchemataFlowFeature{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" />
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> UseFlow<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataBuilder builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        builder.AddFeature<SchemataFlowFeature<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>>();

        return new(builder.Options, builder.Services);
    }

    /// <summary>Enables Flow and configures process definitions through a callback.</summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="configure">Callback that configures the Flow builder.</param>
    /// <returns>The Schemata builder for continued application-level configuration.</returns>
    public static SchemataBuilder UseFlow(
        this SchemataBuilder                                                                                                     builder,
        Action<SchemataFlowBuilder<SchemataProcess, SchemataProcessToken, SchemataProcessTransition, SchemataProcessCompensation, SchemataProcessParticipant, SchemataProcessSource, SchemataFlowEffectIntent>> configure
    ) {
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.UseFlow());
        return builder;
    }

    /// <summary>Enables Flow closed on the host-selected entity types and configures definitions through a callback.</summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="configure">Callback that configures the Flow builder.</param>
    /// <returns>The Schemata builder for continued application-level configuration.</returns>
    public static SchemataBuilder UseFlow<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataBuilder                                                                                        builder,
        Action<SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>> configure
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.UseFlow<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>());
        return builder;
    }
}
