using Schemata.Core;

namespace Schemata.Authorization.Foundation;

/// <summary>
///     Fluent builder for configuring authorization features and OAuth 2.0 flows.
/// </summary>
public sealed class SchemataAuthorizationBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataAuthorizationBuilder" /> class.
    /// </summary>
    public SchemataAuthorizationBuilder(SchemataOptions schemata, Configurators configurators) {
        Schemata      = schemata;
        Configurators = configurators;
    }

    /// <summary>Gets the Schemata framework options.</summary>
    public SchemataOptions Schemata { get; }

    /// <summary>Gets the configurator registry for deferred configuration.</summary>
    public Configurators Configurators { get; }
}
