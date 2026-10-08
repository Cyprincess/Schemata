using System.Linq;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;

namespace Schemata.Resource.Grpc.Runtime;

internal static class GrpcResourceHelper
{
    internal static bool IsGrpcEnabled(ResourceRegistration resource) {
        return resource.Endpoints is null
            || resource.Endpoints.Count == 0
            || resource.Endpoints.Any(e => e == GrpcResourceAttribute.Name);
    }
}
