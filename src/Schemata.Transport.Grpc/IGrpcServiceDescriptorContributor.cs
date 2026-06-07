using System;
using System.Collections.Generic;
using Google.Protobuf.Reflection;

namespace Schemata.Transport.Grpc;

/// <summary>
///     Contributes code-first <see cref="ServiceDescriptor" /> instances to the gRPC
///     server reflection service alongside proto-first ones.
/// </summary>
public interface IGrpcServiceDescriptorContributor
{
    /// <summary>Returns the descriptors to surface via gRPC server reflection.</summary>
    IReadOnlyList<ServiceDescriptor> GetServiceDescriptors(IServiceProvider serviceProvider);
}
