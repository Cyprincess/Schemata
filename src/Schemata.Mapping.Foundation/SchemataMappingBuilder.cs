using Schemata.Core;

namespace Schemata.Mapping.Foundation;

public class SchemataMappingBuilder(SchemataBuilder builder)
{
    public SchemataBuilder Builder { get; } = builder;
}
