// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using Schemata.Abstractions;

namespace Schemata;

public static class ModularOptionsExtensions
{
    public static List<Type>? GetModules(this SchemataOptions options) {
        return options.Get<List<Type>>(Constants.Options.ModularModules);
    }

    public static void SetModules(this SchemataOptions options, List<Type>? value) {
        options.Set(Constants.Options.ModularModules, value);
    }
}
