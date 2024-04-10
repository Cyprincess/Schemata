using Schemata.Core;

namespace Schemata.Mapping.Foundation;

public sealed class SchemataMappingBuilder(SchemataBuilder builder)
{
    public SchemataBuilder Builder { get; } = builder;
}
