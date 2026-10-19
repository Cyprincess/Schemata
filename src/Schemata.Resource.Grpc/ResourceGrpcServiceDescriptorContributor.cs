using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
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
        return FileDescriptorBridge.BuildServiceDescriptors(config);
    }

    #endregion
}
