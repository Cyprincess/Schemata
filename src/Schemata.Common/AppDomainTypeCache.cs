using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Schemata.Common;

/// <summary>
///     Thread-safe cache of all assemblies, exported types, and property metadata in the current AppDomain.
/// </summary>
public static class AppDomainTypeCache
{
    /// <summary>
    ///     Cache of assemblies keyed by assembly name.
    /// </summary>
    public static readonly ConcurrentDictionary<string, Assembly> Assemblies;

    /// <summary>
    ///     Cache of types keyed by full type name.
    /// </summary>
    public static readonly ConcurrentDictionary<string, Type> Types;

    /// <summary>
    ///     Cache of public instance property dictionaries keyed by type handle.
    /// </summary>
    public static readonly ConcurrentDictionary<RuntimeTypeHandle, Dictionary<string, PropertyInfo>> Properties;

    private static readonly ConcurrentDictionary<RuntimeTypeHandle, PropertyInfo[]> WritableProperties = [];

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

    /// <summary>
    ///     Gets an assembly by name, searching loaded assemblies if not cached.
    /// </summary>
    /// <param name="name">The assembly name.</param>
    /// <returns>The assembly, or <see langword="null" /> if not found.</returns>
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

    /// <summary>
    ///     Gets a type by full name, searching all cached assemblies if not found.
    /// </summary>
    /// <param name="name">The full type name.</param>
    /// <returns>The type, or <see langword="null" /> if not found.</returns>
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

    /// <summary>
    ///     Gets all public instance properties for the specified type, cached by type handle.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>A dictionary of property names to property info.</returns>
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

    /// <summary>
    ///     Gets all writable public instance properties for the specified type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>An array of readable and writable properties.</returns>
    public static PropertyInfo[] GetWritableProperties(Type type) {
        return WritableProperties.GetOrAdd(type.TypeHandle,
                                           _ => GetProperties(type)
                                               .Values.Where(p => p is { CanRead: true, CanWrite: true })
                                               .ToArray());
    }

    /// <summary>
    ///     Gets a single property by name for the specified type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The property info, or <see langword="null" /> if not found.</returns>
    public static PropertyInfo? GetProperty(Type type, string name) {
        var properties = GetProperties(type);

        return properties.TryGetValue(name, out var property) ? property : null;
    }
}
