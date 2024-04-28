using Schemata.Core;

namespace Schemata.Authorization.Foundation;

public sealed class SchemataAuthorizationBuilder(SchemataOptions schemata, Configurators configurators)
{
    public SchemataOptions Schemata => schemata;

    public Configurators Configurators => configurators;
}
