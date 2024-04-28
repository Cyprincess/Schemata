using Schemata.Resource.Foundation;

namespace Schemata.Resource.Http;

public sealed class SchemataHttpResourceBuilder(SchemataResourceBuilder builder)
{
    public SchemataResourceBuilder Builder => builder;
}
