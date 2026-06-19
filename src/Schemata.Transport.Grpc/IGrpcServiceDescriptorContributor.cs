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
    /// <summary>
    ///     Returns descriptors to surface via gRPC server reflection.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <returns>The contributed service descriptors.</returns>
    IReadOnlyList<ServiceDescriptor> GetServiceDescriptors(IServiceProvider serviceProvider);
}
