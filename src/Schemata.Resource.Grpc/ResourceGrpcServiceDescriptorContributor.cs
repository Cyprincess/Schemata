using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
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
        var config  = serviceProvider.GetRequiredService<ResourceBinderConfiguration>();
        var options = serviceProvider.GetRequiredService<IOptions<SchemataResourceOptions>>();

        var types = options.Value.Resources
                           .Where(r => r.Value.Endpoints is null
                                    || r.Value.Endpoints.Count == 0
                                    || r.Value.Endpoints.Any(e => e == GrpcResourceAttribute.Name))
                           .Select(r => typeof(IResourceService<,,,>).MakeGenericType(r.Value.Entity, r.Value.Request!, r.Value.Detail!, r.Value.Summary!))
                           .ToArray();

        if (types.Length == 0) {
            return [];
        }

        return FileDescriptorBridge.BuildServiceDescriptors(config.Model, types);
    }

    #endregion
}
