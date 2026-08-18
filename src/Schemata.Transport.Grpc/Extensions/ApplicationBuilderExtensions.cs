using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods configuring the shared gRPC transport wire format.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    ///     Configures <see cref="RuntimeTypeModel.Default" /> with the AIP-standard request types and
    ///     with every type contributed by an <see cref="IProtoTypeContributor" />.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseSchemataProtoModel(this IApplicationBuilder app) {
        SchemataProtoModelConfigurator.ConfigureType(RuntimeTypeModel.Default, typeof(ListRequest));
        SchemataProtoModelConfigurator.ConfigureType(RuntimeTypeModel.Default, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(RuntimeTypeModel.Default, typeof(DeleteRequest));

        var sp           = app.ApplicationServices;
        var contributors = sp.GetServices<IProtoTypeContributor>().ToList();
        if (contributors.Count == 0) {
            return app;
        }

        var summaries = contributors.SelectMany(c => c.GetSummaryTypes(sp)).Distinct().ToList();
        if (summaries.Count > 0) {
            SchemataProtoModelConfigurator.ConfigureSummaryTypes(RuntimeTypeModel.Default, summaries);
        }

        var messages = contributors.SelectMany(c => c.GetMessageTypes(sp)).Distinct().ToList();
        foreach (var type in messages) {
            SchemataProtoModelConfigurator.ConfigureType(RuntimeTypeModel.Default, type);
        }

        return app;
    }
}
