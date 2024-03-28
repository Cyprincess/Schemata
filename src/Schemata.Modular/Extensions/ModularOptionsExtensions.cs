using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Abstractions.Modular;

// ReSharper disable once CheckNamespace
namespace Schemata.Core;

public static class ModularOptionsExtensions
{
    public static List<ModuleDescriptor>? GetModules(this SchemataOptions options) {
        return options.Get<List<ModuleDescriptor>>(Constants.Options.ModularModules);
    }

    public static void SetModules(this SchemataOptions options, List<ModuleDescriptor>? value) {
        options.Set(Constants.Options.ModularModules, value);
    }
}
