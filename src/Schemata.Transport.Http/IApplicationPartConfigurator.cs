using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace Schemata.Transport.Http;

/// <summary>
///     Contributes an <see cref="ApplicationPart" /> or
///     <see cref="IApplicationFeatureProvider" /> to MVC's
///     <see cref="ApplicationPartManager" />.
/// </summary>
public interface IApplicationPartConfigurator
{
    /// <summary>Applies this configurator to <paramref name="manager" />.</summary>
    void Configure(ApplicationPartManager manager);
}
