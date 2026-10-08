using System.Collections.Generic;
using Google.Protobuf.Reflection;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Resource.Grpc;

internal static class FileDescriptorBridge
{
    public static IReadOnlyList<ServiceDescriptor> BuildServiceDescriptors(ResourceBinderConfiguration configuration) =>
        GrpcSchema.Build(configuration.Model, configuration.Methods);
}
