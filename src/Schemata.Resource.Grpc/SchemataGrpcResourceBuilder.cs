using Schemata.Resource.Foundation;

namespace Schemata.Resource.Grpc;

public sealed class SchemataGrpcResourceBuilder
{
    public SchemataGrpcResourceBuilder(SchemataResourceBuilder builder) { Builder = builder; }

    public SchemataResourceBuilder Builder { get; }
}
