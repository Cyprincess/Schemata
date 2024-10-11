using Schemata.Core;

namespace Schemata.Authorization.Foundation;

public sealed class SchemataAuthorizationBuilder
{
    public SchemataAuthorizationBuilder(SchemataOptions schemata, Configurators configurators) {
        Schemata      = schemata;
        Configurators = configurators;
    }

    public SchemataOptions Schemata { get; }

    public Configurators Configurators { get; }
}
