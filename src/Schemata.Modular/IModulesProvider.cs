using System.Collections.Generic;

namespace Schemata.Modular;

/// <summary>
///     Discovers and provides the set of modules available to the application.
/// </summary>
public interface IModulesProvider
{
    /// <summary>
    ///     Returns descriptors for all discovered modules.
    /// </summary>
    /// <returns>An enumerable of module descriptors.</returns>
    IEnumerable<ModuleDescriptor> GetModules();
}
