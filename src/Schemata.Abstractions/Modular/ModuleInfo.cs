using System;
using System.Reflection;

namespace Schemata.Abstractions.Modular;

public class ModuleInfo(
    string   name,
    Assembly assembly,
    Type     entry,
    string?  display     = null,
    string?  description = null,
    string?  version     = null)
{
    public string Name { get; private set; } = name;

    public string DisplayName { get; private set; } = display ?? name;

    public string? Description { get; private set; } = description;

    public string? Version { get; private set; } = version;

    public Assembly Assembly { get; private set; } = assembly;

    public Type EntryType { get; private set; } = entry;
}
