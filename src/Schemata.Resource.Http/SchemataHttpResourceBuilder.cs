using Schemata.Resource.Foundation;

namespace Schemata.Resource.Http;

public sealed class SchemataHttpResourceBuilder : SchemataResourceBuilder
{
    public SchemataHttpResourceBuilder(SchemataResourceBuilder builder) : base(builder.Builder) {
        Builder = builder;
    }

    public new SchemataResourceBuilder Builder { get; }
}
