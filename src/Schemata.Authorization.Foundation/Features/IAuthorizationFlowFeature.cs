using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>Marks a class as an authorization flow feature that can be registered and ordered in the DI pipeline.</summary>
/// <remarks>
///     Flow features are sorted by <see cref="Order" /> and executed in sequence during feature configuration.
///     Extensions like <c>UseCodeFlow()</c> call
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}.AddFlowFeature{T}" />
///     to register implementations of this interface.
/// </remarks>
/// <seealso cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" />
public interface IAuthorizationFlowFeature
{
    /// <summary>The execution order (lower values run first).</summary>
    int Order { get; }

    /// <summary>Configures the services for this flow feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata root options.</param>
    /// <param name="configurators">The configurators collection for deferred configuration.</param>
    void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators);
}
