using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.Building;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using ProtoServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Contributes the code-first <see cref="ProtoServiceDescriptor" /> instances built by
///     <see cref="FileDescriptorBridge" /> for every resource registered as a gRPC endpoint
///     to <see cref="Schemata.Transport.Grpc.Features.SchemataTransportGrpcFeature" />'s
///     reflection service.
/// </summary>
internal sealed class ResourceGrpcServiceDescriptorContributor : IGrpcServiceDescriptorContributor
{
    #region IGrpcServiceDescriptorContributor Members

    public IReadOnlyList<ProtoServiceDescriptor> GetServiceDescriptors(IServiceProvider serviceProvider) {
        var config   = serviceProvider.GetRequiredService<ResourceBinderConfiguration>();
        var registry = serviceProvider.GetRequiredService<ResourceRegistry>();

        var types = registry.Resources
                            .Where(GrpcResourceHelper.IsGrpcEnabled)
                            .Select(r => typeof(IResourceService<,,,>).MakeGenericType(r.Entity, r.Request!, r.Detail!, r.Summary!))
                            .ToArray();

        if (types.Length == 0) {
            return [];
        }

        return FileDescriptorBridge.BuildServiceDescriptors(config.Model, types, registry);
    }

    #endregion
}
