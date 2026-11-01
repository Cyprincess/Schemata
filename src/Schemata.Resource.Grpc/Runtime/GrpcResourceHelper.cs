using Schemata.Abstractions.Resource;
using Schemata.Core.Building;

namespace Schemata.Resource.Grpc.Runtime;

internal static class GrpcResourceHelper
{
    internal static bool IsGrpcEnabled(ResourceRegistration resource) {
        return resource.IsEndpointEnabled(GrpcResourceAttribute.Name);
    }
}
