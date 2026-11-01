using ProtoBuf.Meta;

namespace Schemata.Transport.Grpc;

public interface IGrpcRuntimeModelContributor
{
    void Configure(RuntimeTypeModel model);
}
