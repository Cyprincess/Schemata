using System;
using System.Collections.Generic;
using System.Reflection;

namespace Schemata.Abstractions;

public static class AppDomainTypeCache
{
    static AppDomainTypeCache() {
        Assemblies = [];
        Types      = [];

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName();
            if (string.IsNullOrWhiteSpace(name.Name)) {
                continue;
            }

            Assemblies[name.Name] = assembly;

            foreach (var type in assembly.GetExportedTypes()) {
                if (string.IsNullOrWhiteSpace(type.FullName)) {
                    continue;
                }

                Types[type.FullName] = type;
            }
        }
    }

    public static Dictionary<string, Assembly> Assemblies { get; }

    public static Dictionary<string, Type> Types { get; }
}
