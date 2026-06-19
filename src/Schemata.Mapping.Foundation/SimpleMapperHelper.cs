using System;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Common;
using Schemata.Mapping.Skeleton;

namespace Schemata.Mapping.Foundation;

/// <summary>
///     Helper for field-selective mapping that preserves destination values for unmasked fields.
/// </summary>
public static class SimpleMapperHelper
{
    /// <summary>
    ///     Maps source to destination using the provided action, but only updates fields in the mask.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="destination">The destination object.</param>
    /// <param name="mask">The CLR field names or dot paths to update; all other fields retain their pre-map values.</param>
    /// <param name="mapAction">The mapping action to invoke.</param>
    /// <remarks>
    ///     Saves the values of non-masked writable properties before mapping, then restores them afterward.
    /// </remarks>
    public static void MapWithMask<TSource, TDestination>(
        TSource                       source,
        TDestination                  destination,
        IEnumerable<string>           mask,
        Action<TSource, TDestination> mapAction
    ) {
        if (source is null || destination is null) {
            mapAction(source, destination);
            return;
        }

        var tree       = MaskTree.FromClr(typeof(TDestination), mask, false);
        var properties = AppDomainTypeCache.GetWritableProperties(typeof(TDestination));
        var saved      = new List<PropertySnapshot>();
        var interiors  = new List<InteriorSnapshot>();

        foreach (var property in properties) {
            if (!tree.Children.TryGetValue(property.Name, out var node)) {
                saved.Add(new([property], property.GetValue(destination)));
                continue;
            }

            if (node.IsLeaf) {
                continue;
            }

            var value = property.GetValue(destination);
            interiors.Add(new(property, value, node));
            if (value is not null) {
                MaskWalker.WalkUnmasked(value, node.Children, [property], false,
                                        (prefix, target, prop) => saved.Add(new(MaskWalker.Append(prefix, prop), prop.GetValue(target))));
            }
        }

        mapAction(source, destination);

        foreach (var snapshot in saved) {
            Restore(destination, snapshot.Path, snapshot.Value);
        }

        foreach (var interior in interiors) {
            var current = interior.Property.GetValue(destination);
            if (current is null && interior.Value is not null) {
                interior.Property.SetValue(destination, interior.Value);
                CopyMaskedLeaves(source, destination, [interior.Property], interior.Node, static _ => true);
            } else if (current is not null && interior.Value is null) {
                // A newly populated interior can carry unmasked nested values; keep the masked leaves only.
                MaskWalker.WalkUnmasked(current, interior.Node.Children, false, static (_, target, prop) => SetDefault(target, prop));
            }
        }

        CopyMaskedLeaves(source, destination, typeof(TSource), typeof(TDestination), tree.Children, IsUnpopulated);
    }

    /// <summary>
    ///     Maps source onto destination treating null and whitespace-only source members as
    ///     unpopulated: their destination values are preserved. This gives merge mapping the
    ///     AIP-134 implicit-mask semantics — an update that omits <c>update_mask</c>
    ///     touches populated fields, while <see cref="MapWithMask{TSource, TDestination}" />
    ///     stays authoritative and can clear fields.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="destination">The destination object.</param>
    /// <param name="mapAction">The mapping action to invoke.</param>
    public static void MapMerging<TSource, TDestination>(
        TSource                       source,
        TDestination                  destination,
        Action<TSource, TDestination> mapAction
    ) {
        if (source is null || destination is null) {
            mapAction(source, destination);
            return;
        }

        MapMergingCore(source, destination, typeof(TSource), typeof(TDestination), () => mapAction(source, destination));
    }

    /// <summary>
    ///     Maps source onto destination using runtime source and destination types while preserving
    ///     destination values for unpopulated source members.
    /// </summary>
    /// <param name="source">The source object.</param>
    /// <param name="destination">The destination object.</param>
    /// <param name="sourceType">The runtime source type.</param>
    /// <param name="destinationType">The runtime destination type.</param>
    /// <param name="mapAction">The mapping action to invoke.</param>
    public static void MapMerging(
        object source,
        object destination,
        Type   sourceType,
        Type   destinationType,
        Action mapAction
    ) {
        MapMergingCore(source, destination, sourceType, destinationType, mapAction);
    }

    private static void MapMergingCore(
        object source,
        object destination,
        Type   sourceType,
        Type   destinationType,
        Action mapAction
    ) {
        var sources  = AppDomainTypeCache.GetProperties(sourceType);
        var writable = AppDomainTypeCache.GetWritableProperties(destinationType);

        var saved = new (PropertyInfo Prop, object? Value)[writable.Length];
        var count = 0;
        foreach (var property in writable) {
            if (!sources.TryGetValue(property.Name, out var src) || !src.CanRead) continue;
            if (!IsUnpopulated(src.GetValue(source))) continue;

            saved[count++] = (property, property.GetValue(destination));
        }

        mapAction();

        for (var i = 0; i < count; i++) {
            saved[i].Prop.SetValue(destination, saved[i].Value);
        }
    }

    private static bool IsUnpopulated(object? value) {
        return value is null || (value is string s && string.IsNullOrWhiteSpace(s));
    }

    private static bool AcceptsNull(Type type) {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
    }

    private static void SetDefault(object target, PropertyInfo property) {
        var @default = property.PropertyType.IsValueType
            ? Activator.CreateInstance(property.PropertyType)
            : null;
        property.SetValue(target, @default);
    }

    private static void CopyMaskedLeaves(
        object                      source,
        object                      destination,
        IReadOnlyList<PropertyInfo> prefix,
        MaskTree.MaskNode           node,
        Func<object?, bool>         shouldCopy
    ) {
        var srcRoot = GetValueByNames(source, prefix);
        var dstRoot = GetValue(destination, prefix);
        if (srcRoot is null || dstRoot is null) {
            return;
        }

        CopyMaskedLeaves(srcRoot, dstRoot, srcRoot.GetType(), dstRoot.GetType(), node.Children, shouldCopy);
    }

    private static void CopyMaskedLeaves(
        object                                         source,
        object                                         destination,
        Type                                           sourceType,
        Type                                           destinationType,
        IReadOnlyDictionary<string, MaskTree.MaskNode> nodes,
        Func<object?, bool>                            shouldCopy
    ) {
        var sources      = AppDomainTypeCache.GetProperties(sourceType);
        var destinations = AppDomainTypeCache.GetProperties(destinationType);

        foreach (var node in nodes.Values) {
            if (!sources.TryGetValue(node.Name, out var src) || !src.CanRead) continue;
            if (!destinations.TryGetValue(node.Name, out var dst) || !dst.CanWrite) continue;

            if (node.IsLeaf) {
                var value = src.GetValue(source);
                if (!shouldCopy(value)) continue;
                if (CanAssign(dst.PropertyType, value)) {
                    dst.SetValue(destination, value);
                }

                continue;
            }

            var srcValue = src.GetValue(source);
            var dstValue = dst.GetValue(destination);
            if (srcValue is null || dstValue is null) continue;

            CopyMaskedLeaves(srcValue, dstValue, src.PropertyType, dst.PropertyType, node.Children, shouldCopy);
        }
    }

    private static bool CanAssign(Type type, object? value) {
        return value is null ? AcceptsNull(type) : type.IsInstanceOfType(value);
    }

    private static object? GetValue(object root, IReadOnlyList<PropertyInfo> path) {
        object? current = root;
        foreach (var property in path) {
            if (current is null) {
                return null;
            }

            current = property.GetValue(current);
        }

        return current;
    }

    private static object? GetValueByNames(object root, IReadOnlyList<PropertyInfo> path) {
        object? current = root;
        var type = root.GetType();
        foreach (var property in path) {
            if (current is null) {
                return null;
            }

            var properties = AppDomainTypeCache.GetProperties(type);
            if (!properties.TryGetValue(property.Name, out var currentProperty) || !currentProperty.CanRead) {
                return null;
            }

            current = currentProperty.GetValue(current);
            type    = currentProperty.PropertyType;
        }

        return current;
    }

    private static void Restore(object root, IReadOnlyList<PropertyInfo> path, object? value) {
        object? current = root;
        for (var i = 0; i < path.Count - 1; i++) {
            current = path[i].GetValue(current);
            if (current is null) {
                return;
            }
        }

        path[^1].SetValue(current, value);
    }

    private sealed record PropertySnapshot(IReadOnlyList<PropertyInfo> Path, object? Value);

    private sealed record InteriorSnapshot(PropertyInfo Property, object? Value, MaskTree.MaskNode Node);
}
