using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace Schemata.Transport.Http;

/// <summary>
///     Contributes an <see cref="ApplicationPart" /> or
///     <see cref="IApplicationFeatureProvider" /> to MVC's
///     <see cref="ApplicationPartManager" />.
/// </summary>
public interface IApplicationPartConfigurator
{
    /// <summary>
    ///     Applies this configurator to the application part manager.
    /// </summary>
    /// <param name="manager">The application part manager.</param>
    void Configure(ApplicationPartManager manager);
}
