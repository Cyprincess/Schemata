using Schemata.Core;

namespace Schemata.Resource.Foundation;

public class SchemataResourceBuilder(SchemataBuilder builder)
{
    public SchemataBuilder Builder { get; } = builder;
}
