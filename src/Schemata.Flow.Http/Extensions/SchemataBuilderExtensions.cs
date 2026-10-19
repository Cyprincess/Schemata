using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Http.Features;
using Schemata.Flow.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>MapHttp</c> extension method on
///     <see cref="SchemataFlowBuilder{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Registers Flow resources for HTTP and adds the definitions-only
    ///     controller from this assembly to MVC discovery.
    /// </summary>
    /// <param name="builder">The Flow builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="SchemataFlowHttpFeature{TProcess,TToken,TTransition}" />
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> MapHttp<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        builder.AddFeature<SchemataFlowHttpFeature<TProcess, TToken, TTransition>>();

        return builder;
    }
}
