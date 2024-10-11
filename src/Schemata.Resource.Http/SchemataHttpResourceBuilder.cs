using Schemata.Resource.Foundation;

namespace Schemata.Resource.Http;

public sealed class SchemataHttpResourceBuilder
{
    public SchemataHttpResourceBuilder(SchemataResourceBuilder builder) {
        Builder = builder;
    }

    public SchemataResourceBuilder Builder { get; }
}
