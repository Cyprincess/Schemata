using System;
using System.Linq;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Grpc.Internal;

internal static class GrpcResourceHelper
{
    internal static Type? FindHandlerInterface(Type handler) {
        foreach (var iface in handler.GetInterfaces()) {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IResourceMethodHandler<,,>)) {
                return iface;
            }
        }
        return null;
    }

    internal static bool IsGrpcEnabled(ResourceAttribute resource) {
        return resource.Endpoints is null
            || resource.Endpoints.Count == 0
            || resource.Endpoints.Any(e => e == GrpcResourceAttribute.Name);
    }
}
