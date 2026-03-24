using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Modular;

// ReSharper disable once CheckNamespace
namespace Schemata.Core;

/// <summary>
/// Extension methods for storing and retrieving module descriptors in <see cref="SchemataOptions"/>.
/// </summary>
public static class SchemataOptionsExtensions
{
    /// <summary>
    /// Gets the list of discovered module descriptors.
    /// </summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <returns>The module descriptors, or <see langword="null"/> if not set.</returns>
    public static List<ModuleDescriptor>? GetModules(this SchemataOptions schemata) {
        return schemata.Get<List<ModuleDescriptor>>(SchemataConstants.Options.ModularModules);
    }

    /// <summary>
    /// Sets the list of discovered module descriptors.
    /// </summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <param name="value">The module descriptors to store.</param>
    public static void SetModules(this SchemataOptions schemata, List<ModuleDescriptor>? value) {
        schemata.Set(SchemataConstants.Options.ModularModules, value);
    }
}
