using System;
using System.Reflection;

namespace Schemata.Abstractions.Modular;

public class ModuleDescriptor
{
    public ModuleDescriptor(
        string   name,
        Assembly assembly,
        Type     entry,
        Type     provider,
        string?  display     = null,
        string?  description = null,
        string?  company     = null,
        string?  copyright   = null,
        string?  version     = null) {
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

    public string Name { get; private set; }

    public string DisplayName { get; private set; }

    public string? Description { get; private set; }

    public string? Company { get; private set; }

    public string? Copyright { get; private set; }

    public string? Version { get; private set; }

    public Assembly Assembly { get; private set; }

    public Type EntryType { get; private set; }

    public Type ProviderType { get; private set; }
}
