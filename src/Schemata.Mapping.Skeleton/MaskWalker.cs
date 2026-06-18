using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Common;

namespace Schemata.Mapping.Skeleton;

/// <summary>
///     Shared traversal of an object graph against a parsed <see cref="MaskTree" />. Visits every
///     writable property the mask does <em>not</em> name (the "unmasked" properties), recursing into
///     masked interior objects. Response read-mask trimming and field-selective mapping share this so
///     the traversal lives in one place instead of being re-implemented per call site.
/// </summary>
public static class MaskWalker
{
    /// <summary>
    ///     Invokes <paramref name="onUnmasked" /> for every writable property of <paramref name="target" />
    ///     (and of its masked-interior descendants) that <paramref name="nodes" /> does not name.
    /// </summary>
    /// <param name="target">The object to walk; its runtime type drives property discovery.</param>
    /// <param name="nodes">Mask nodes for <paramref name="target" />, keyed by CLR property name.</param>
    /// <param name="traverseCollections">Whether to recurse into the elements of masked collection properties.</param>
    /// <param name="onUnmasked">Receives the path prefix to the containing object, the object, and the unmasked property.</param>
    public static void WalkUnmasked(
        object                                                    target,
        IReadOnlyDictionary<string, MaskTree.MaskNode>            nodes,
        bool                                                      traverseCollections,
        Action<IReadOnlyList<PropertyInfo>, object, PropertyInfo> onUnmasked
    ) {
        WalkUnmasked(target, nodes, [], traverseCollections, onUnmasked);
    }

    /// <summary>
    ///     As <see cref="WalkUnmasked(object, IReadOnlyDictionary{string, MaskTree.MaskNode}, bool, Action{IReadOnlyList{PropertyInfo}, object, PropertyInfo})" />,
    ///     but seeded with the path of <paramref name="target" /> relative to a larger graph so callers
    ///     that record root-relative paths see the full chain.
    /// </summary>
    /// <param name="target">The object to walk.</param>
    /// <param name="nodes">Mask nodes for <paramref name="target" />.</param>
    /// <param name="prefix">The property path from the root graph to <paramref name="target" />.</param>
    /// <param name="traverseCollections">Whether to recurse into the elements of masked collection properties.</param>
    /// <param name="onUnmasked">The visitor for each unmasked property.</param>
    public static void WalkUnmasked(
        object                                                    target,
        IReadOnlyDictionary<string, MaskTree.MaskNode>            nodes,
        IReadOnlyList<PropertyInfo>                               prefix,
        bool                                                      traverseCollections,
        Action<IReadOnlyList<PropertyInfo>, object, PropertyInfo> onUnmasked
    ) {
        var properties = AppDomainTypeCache.GetWritableProperties(target.GetType());
        foreach (var property in properties) {
            if (!nodes.TryGetValue(property.Name, out var node)) {
                onUnmasked(prefix, target, property);
                continue;
            }

            if (node.IsLeaf) {
                continue;
            }

            var value = property.GetValue(target);
            if (value is null) {
                continue;
            }

            var path = Append(prefix, property);
            if (traverseCollections && value is IEnumerable items && value is not string) {
                foreach (var item in items) {
                    if (item is not null) {
                        WalkUnmasked(item, node.Children, path, traverseCollections, onUnmasked);
                    }
                }

                continue;
            }

            WalkUnmasked(value, node.Children, path, traverseCollections, onUnmasked);
        }
    }

    /// <summary>Returns <paramref name="prefix" /> extended by <paramref name="property" />.</summary>
    /// <param name="prefix">The existing path.</param>
    /// <param name="property">The property to append.</param>
    /// <returns>A new path array.</returns>
    public static PropertyInfo[] Append(IReadOnlyList<PropertyInfo> prefix, PropertyInfo property) {
        var path = new PropertyInfo[prefix.Count + 1];
        for (var i = 0; i < prefix.Count; i++) {
            path[i] = prefix[i];
        }

        path[^1] = property;
        return path;
    }
}
