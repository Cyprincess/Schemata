using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Abstractions.Modular;

// ReSharper disable once CheckNamespace
namespace Schemata.Core;

public static class SchemataOptionsExtensions
{
    public static List<ModuleDescriptor>? GetModules(this SchemataOptions schemata) {
        return schemata.Get<List<ModuleDescriptor>>(SchemataConstants.Options.ModularModules);
    }

    public static void SetModules(this SchemataOptions schemata, List<ModuleDescriptor>? value) {
        schemata.Set(SchemataConstants.Options.ModularModules, value);
    }
}
