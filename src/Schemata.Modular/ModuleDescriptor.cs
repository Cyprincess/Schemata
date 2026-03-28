using System;
using System.Reflection;

namespace Schemata.Modular;

/// <summary>
///     Describes a discovered module, including its assembly metadata and entry point type.
/// </summary>
public class ModuleDescriptor
{
    /// <summary>
    ///     Initializes a new module descriptor.
    /// </summary>
    /// <param name="name">The module assembly name.</param>
    /// <param name="assembly">The loaded assembly.</param>
    /// <param name="entry">The <see cref="Schemata.Abstractions.Modular.ModuleBase" /> implementation type.</param>
    /// <param name="provider">The provider type that discovered this module.</param>
    /// <param name="display">The display name, defaults to <paramref name="name" />.</param>
    /// <param name="description">An optional description from assembly metadata.</param>
    /// <param name="company">The company name from assembly metadata.</param>
    /// <param name="copyright">The copyright notice, auto-generated if not provided.</param>
    /// <param name="version">The informational version string.</param>
    public ModuleDescriptor(
        string   name,
        Assembly assembly,
        Type     entry,
        Type     provider,
        string?  display     = null,
        string?  description = null,
        string?  company     = null,
        string?  copyright   = null,
        string?  version     = null
    ) {
        Name         = name;
        DisplayName  = display ?? name;
        Description  = description;
        Company      = company;
        Copyright    = copyright ?? $"\u00a9 {DateTime.Now.Year} {company}";
        Version      = version;
        Assembly     = assembly;
        EntryType    = entry;
        ProviderType = provider;
    }

    /// <summary>
    ///     The module assembly name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    ///     The human-readable display name.
    /// </summary>
    public string DisplayName { get; private set; }

    /// <summary>
    ///     An optional description of the module.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    ///     The company that produced the module.
    /// </summary>
    public string? Company { get; private set; }

    /// <summary>
    ///     The copyright notice for the module.
    /// </summary>
    public string? Copyright { get; private set; }

    /// <summary>
    ///     The informational version string, truncated for display.
    /// </summary>
    public string? Version { get; private set; }

    /// <summary>
    ///     The loaded assembly containing the module.
    /// </summary>
    public Assembly Assembly { get; private set; }

    /// <summary>
    ///     The concrete <see cref="Schemata.Abstractions.Modular.ModuleBase" /> implementation type.
    /// </summary>
    public Type EntryType { get; private set; }

    /// <summary>
    ///     The <see cref="IModulesProvider" /> type that discovered this module.
    /// </summary>
    public Type ProviderType { get; private set; }
}
