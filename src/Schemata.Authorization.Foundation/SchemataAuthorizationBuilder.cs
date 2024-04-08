using Schemata.Core;

namespace Schemata.Authorization.Foundation;

public class SchemataAuthorizationBuilder(SchemataBuilder builder)
{
    public SchemataBuilder Builder { get; } = builder;
}
