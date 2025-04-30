using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Schemata.Abstractions;

public static class AppDomainTypeCache
{
    public static readonly ConcurrentDictionary<string, Assembly> Assemblies;

    public static readonly ConcurrentDictionary<string, Type> Types;

    public static readonly ConcurrentDictionary<RuntimeTypeHandle, Dictionary<string, PropertyInfo>> Properties;

    static AppDomainTypeCache() {
        Assemblies = [];
        Types      = [];
        Properties = [];

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (assembly.IsDynamic) {
                continue;
            }

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

    public static Dictionary<string, PropertyInfo> GetProperties(Type type) {
        if (Properties.TryGetValue(type.TypeHandle, out var properties)) {
            return properties;
        }

        properties = type.GetProperties(
                              BindingFlags.GetProperty
                            | BindingFlags.IgnoreCase
                            | BindingFlags.Public
                            | BindingFlags.Instance)
                         .ToDictionary(m => m.Name, m => m);
        Properties[type.TypeHandle] = properties;

        return properties;
    }

    public static PropertyInfo? GetProperty(Type type, string name) {
        var properties = GetProperties(type);

        return properties.TryGetValue(name, out var property) ? property : null;
    }
}
