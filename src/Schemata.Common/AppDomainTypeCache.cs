using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Schemata.Common;

/// <summary>
///     Thread-safe cache of all assemblies, exported types, and property and field metadata in the
///     current <see cref="AppDomain" />.
/// </summary>
/// <remarks>
///     The cache stores type metadata only and never holds request-scoped state, so one instance is
///     safe to share across requests and tenants. Assembly discovery is incremental: accessors that
///     resolve an assembly or type re-scan <see cref="AppDomain.CurrentDomain" /> and index only the
///     assemblies that were not indexed before, so assemblies loaded after the first touch become
///     visible while entries indexed by earlier scans are never cleared.
///     <para>
///         Closed-generic method reflection (<see cref="Type.GetMethod(string, Type[])" /> and
///         <see cref="MethodInfo.MakeGenericMethod(Type[])" />) stays direct this round: decision D10
///         keeps method metadata out of the cache unless a measurement shows the lookup cost matters,
///         so this class exposes no method accessors.
///     </para>
/// </remarks>
public static class AppDomainTypeCache
{
    /// <summary>
    ///     Assemblies keyed by assembly name.
    /// </summary>
    public static readonly ConcurrentDictionary<string, Assembly> Assemblies;

    /// <summary>
    ///     Exported types keyed by full type name.
    /// </summary>
    public static readonly ConcurrentDictionary<string, Type> Types;

    /// <summary>
    ///     Public instance property dictionaries keyed by type handle.
    /// </summary>
    public static readonly ConcurrentDictionary<RuntimeTypeHandle, Dictionary<string, PropertyInfo>> Properties;

    private static readonly ConcurrentDictionary<RuntimeTypeHandle, Dictionary<string, FieldInfo>> Fields = new();

    private static readonly ConcurrentDictionary<Assembly, byte> IndexedAssemblies = new();

    private static readonly ConcurrentDictionary<RuntimeTypeHandle, Dictionary<string, PropertyInfo>> StaticProperties = new();

    private static readonly ConcurrentDictionary<RuntimeTypeHandle, PropertyInfo[]> WritableProperties = [];

    static AppDomainTypeCache() {
        Assemblies = [];
        Types      = [];
        Properties = [];

        Refresh();
    }

    /// <summary>
    ///     Gets every exported type from the assemblies loaded in the current <see cref="AppDomain" />,
    ///     re-scanning assemblies that were loaded after the previous scan.
    /// </summary>
    /// <returns>A snapshot of the cached exported types.</returns>
    public static IEnumerable<Type> GetAllTypes() {
        Refresh();

        return Types.Values;
    }

    /// <summary>
    ///     Gets an assembly by name, searching all loaded assemblies if not already cached.
    /// </summary>
    /// <param name="name">The assembly name.</param>
    /// <returns>The assembly, or <see langword="null" /> if not found.</returns>
    public static Assembly? GetAssembly(string name) {
        if (Assemblies.TryGetValue(name, out var assembly)) {
            return assembly;
        }

        Refresh();

        if (Assemblies.TryGetValue(name, out assembly)) {
            return assembly;
        }

        assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
        if (assembly is null) {
            return null;
        }

        Assemblies[name] = assembly;
        return assembly;
    }

    /// <summary>
    ///     Gets a type by full name, searching all cached assemblies if not found by
    ///     <see cref="Type.GetType(string, bool)" />.
    /// </summary>
    /// <param name="name">The full type name.</param>
    /// <returns>The type, or <see langword="null" /> if not found.</returns>
    public static Type? GetType(string name) {
        if (Types.TryGetValue(name, out var type)) {
            return type;
        }

        type = Type.GetType(name, false);
        if (!string.IsNullOrWhiteSpace(type?.FullName)) {
            Types[type.FullName] = type;
            return type;
        }

        Refresh();

        if (Types.TryGetValue(name, out type)) {
            return type;
        }

        foreach (var assembly in Assemblies.Values) {
            type = assembly.GetType(name, false);
            if (string.IsNullOrWhiteSpace(type?.FullName)) {
                continue;
            }

            Types[type.FullName] = type;
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
    ///     Gets all public instance fields for the specified type, cached by type handle.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>A dictionary of field names to field info.</returns>
    public static Dictionary<string, FieldInfo> GetFields(Type type) {
        if (Fields.TryGetValue(type.TypeHandle, out var fields)) {
            return fields;
        }

        fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .ToDictionary(m => m.Name, m => m);
        Fields[type.TypeHandle] = fields;

        return fields;
    }

    /// <summary>
    ///     Gets all writable public instance properties for the specified type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>An array of readable and writable properties.</returns>
    public static PropertyInfo[] GetWritableProperties(Type type) {
        return WritableProperties.GetOrAdd(
            type.TypeHandle,
            _ => GetProperties(type).Values.Where(p => p is { CanRead: true, CanWrite: true }).ToArray()
        );
    }

    /// <summary>
    ///     Gets a single property by name for the specified type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The property info, or <see langword="null" /> if not found.</returns>
    public static PropertyInfo? GetProperty(Type type, string name) {
        var properties = GetProperties(type);

        return properties.GetValueOrDefault(name);
    }

    /// <summary>
    ///     Returns the cached public static property with the specified name, or
    ///     <see langword="null" /> if not found.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The property info, or <see langword="null" /> if not found.</returns>
    public static PropertyInfo? GetStaticProperty(Type type, string name) {
        var properties = StaticProperties.GetOrAdd(
            type.TypeHandle,
            static (_, t) => t.GetProperties(BindingFlags.Public | BindingFlags.Static)
                              .ToDictionary(p => p.Name, StringComparer.Ordinal),
            type);

        return properties.GetValueOrDefault(name);
    }

    private static void Refresh() {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            Index(assembly);
        }
    }

    private static void Index(Assembly assembly) {
        if (assembly.IsDynamic || !IndexedAssemblies.TryAdd(assembly, 0)) {
            return;
        }

        var name = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(name)) {
            return;
        }

        Assemblies[name] = assembly;

        IEnumerable<Type> types;
        try {
            types = assembly.GetExportedTypes();
        } catch (ReflectionTypeLoadException exception) {
            // A late-loaded assembly can reference types whose dependencies are not resolvable yet;
            // indexing the loadable types keeps the rest of the application working.
            types = exception.Types.OfType<Type>();
        }

        foreach (var type in types) {
            if (string.IsNullOrWhiteSpace(type.FullName)) {
                continue;
            }

            Types[type.FullName] = type;
        }
    }
}
