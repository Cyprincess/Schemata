using Schemata.Core;

namespace Schemata.Mapping.Foundation;

public sealed class SchemataMappingBuilder(SchemataOptions schemata, Configurators configurators)
{
    public SchemataOptions Schemata => schemata;

    public Configurators Configurators => configurators;
}
