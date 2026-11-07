using Schemata.Core.Linking;

namespace Schemata.Transport.Grpc.Linking;

public interface IGrpcLinkTargetResolver
{
    GrpcLinkTarget? Resolve(SemanticTarget target);
    SemanticTarget? GetTarget(string method, object request);
}
