// ReSharper disable CheckNamespace

using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Abstractions.Modular;

namespace Schemata.Core;

public static class ModularOptionsExtensions
{
    public static List<ModuleInfo>? GetModules(this SchemataOptions options) {
        return options.Get<List<ModuleInfo>>(Constants.Options.ModularModules);
    }

    public static void SetModules(this SchemataOptions options, List<ModuleInfo>? value) {
        options.Set(Constants.Options.ModularModules, value);
    }
}
