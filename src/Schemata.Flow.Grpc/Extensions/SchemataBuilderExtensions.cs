using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Grpc.Features;
using Schemata.Flow.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>MapGrpc</c> extension method on
///     <see cref="SchemataFlowBuilder{TProcess,TToken,TTransition,TCompensation,TParticipant,TSource,TEffectIntent}" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Registers Flow resources for gRPC and maps the definitions-only gRPC service.
    /// </summary>
    /// <param name="builder">The Flow builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="SchemataFlowGrpcFeature{TProcess,TToken,TTransition}" />
    public static SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> MapGrpc<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(
        this SchemataFlowBuilder<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent> builder
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        builder.AddFeature<SchemataFlowGrpcFeature<TProcess, TToken, TTransition>>();

        return builder;
    }
}
