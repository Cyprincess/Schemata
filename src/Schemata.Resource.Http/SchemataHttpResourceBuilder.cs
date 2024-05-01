using Schemata.Resource.Foundation;

namespace Schemata.Resource.Http;

public sealed class SchemataHttpResourceBuilder(SchemataResourceBuilder builder) : SchemataResourceBuilder(builder.Builder)
{
    public new SchemataResourceBuilder Builder { get; } = builder;
}
