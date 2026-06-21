using Schemata.Insight.Foundation;
using Schemata.Insight.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling gRPC transport on the Insight builder.
/// </summary>
public static class SchemataInsightGrpcBuilderExtensions
{
    /// <summary>
    ///     Exposes the Insight query endpoint over gRPC and returns the same builder so registration
    ///     chains.
    /// </summary>
    /// <param name="builder">The Insight builder.</param>
    /// <returns>The Insight builder for chaining.</returns>
    public static SchemataInsightBuilder MapGrpc(this SchemataInsightBuilder builder) {
        builder.AddFeature<SchemataInsightGrpcFeature>();

        return builder;
    }
}
