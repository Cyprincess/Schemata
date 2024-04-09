using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Abstractions.Modular;

// ReSharper disable once CheckNamespace
namespace Schemata.Core;

public static class ModularOptionsExtensions
{
    public static List<ModuleDescriptor>? GetModules(this SchemataOptions schemata) {
        return schemata.Get<List<ModuleDescriptor>>(Constants.Options.ModularModules);
    }

    public static void SetModules(this SchemataOptions schemata, List<ModuleDescriptor>? value) {
        schemata.Set(Constants.Options.ModularModules, value);
    }
}
