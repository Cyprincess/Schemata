using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace Schemata.Abstractions;

public static class AppDomainTypeCache
{
    public static readonly ConcurrentDictionary<string, Assembly> Assemblies;

    public static readonly ConcurrentDictionary<string, Type> Types;

    static AppDomainTypeCache() {
        Assemblies = [];
        Types      = [];

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            Assemblies[name] = assembly;

            var types = assembly.GetExportedTypes();
            foreach (var type in types) {
                if (string.IsNullOrWhiteSpace(type.FullName)) {
                    continue;
                }

                Types[type.FullName] = type;
            }
        }
    }

    public static Assembly? GetAssembly(string name) {
        if (Assemblies.TryGetValue(name, out var assembly)) {
            return assembly;
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        assembly = assemblies.FirstOrDefault(a => a.GetName().Name == name);
        if (assembly is null) {
            return null;
        }

        Assemblies[name] = assembly;
        return assembly;
    }

    public static Type? GetType(string name) {
        if (Types.TryGetValue(name, out var type)) {
            return type;
        }

        type = Type.GetType(name, false);
        if (!string.IsNullOrWhiteSpace(type?.FullName)) {
            Types[type!.FullName] = type;
            return type;
        }

        foreach (var assembly in Assemblies.Values) {
            type = assembly.GetType(name, false);
            if (string.IsNullOrWhiteSpace(type?.FullName)) {
                continue;
            }

            Types[type!.FullName] = type;
            return type;
        }

        return null;
    }
}
