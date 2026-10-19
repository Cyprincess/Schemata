using System;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Common;
using Schemata.Mapping.Skeleton;

namespace Schemata.Mapping.Foundation;

/// <summary>
///     Helpers for field-selective mapping: <see cref="MapWithMask{TSource,TDestination}" /> applies
///     an explicit AIP-161 field mask without mapping the real destination, and
///     <see cref="MapMerging{TSource,TDestination}" /> gives plain merges the AIP-134 implicit mask.
/// </summary>
public static class SimpleMapperHelper
{
    /// <summary>
    ///     Applies to the destination only the members selected by <paramref name="mask" />, taking
    ///     their values from a projection the mapper builds under the current map configuration.
    ///     The real destination is never mapped, so unmasked mutable members cannot be polluted by
    ///     the engine's in-place writes.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="destination">The destination object.</param>
    /// <param name="mask">The CLR field names or dot paths to update; all other fields retain their values.</param>
    /// <param name="mapAction">
    ///     The engine's map-to-target action, invoked directly for the null, no-mask, and whole-mask
    ///     paths where mapping the destination is safe.
    /// </param>
    /// <param name="project">
    ///     The engine's map-to-fresh-instance action, invoked once to produce the projected values
    ///     the mask selects from.
    /// </param>
    public static void MapWithMask<TSource, TDestination>(
        TSource                       source,
        TDestination                  destination,
        IEnumerable<string>           mask,
        Action<TSource, TDestination> mapAction,
        Func<TSource, TDestination>   project
    ) {
        if (source is null || destination is null) {
            mapAction(source, destination);
            return;
        }

        var tree = MaskTree.FromClr(typeof(TDestination), mask, false);
        if (tree.Children.Count == 0 || SelectsEveryWritableMember(tree, typeof(TDestination))) {
            mapAction(source, destination);
            return;
        }

        var staged = project(source)
                  ?? throw new InvalidOperationException(
                      $"The mapper returned no '{typeof(TDestination).FullName}' projection for the field mask.");

        ApplyMasked(staged, destination, tree.Children);
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
    ///     Copies values from a replacement mapping result back to the original map-to-target
    ///     destination. A no-op when the mapper returns the same reference it was given.
    /// </summary>
    /// <param name="mapped">The instance returned by the map-to-target operation.</param>
    /// <param name="destination">The target instance supplied to the mapper.</param>
    public static void ReconcileReplacement(object? mapped, object? destination) {
        if (mapped is null || destination is null || ReferenceEquals(mapped, destination)) {
            return;
        }

        foreach (var property in AppDomainTypeCache.GetWritableProperties(destination.GetType())) {
            property.SetValue(destination, property.GetValue(mapped));
        }
    }

    private static bool SelectsEveryWritableMember(MaskTree tree, Type destinationType) {
        foreach (var property in AppDomainTypeCache.GetWritableProperties(destinationType)) {
            if (!tree.Children.TryGetValue(property.Name, out var node) || !node.IsLeaf) {
                return false;
            }
        }

        return true;
    }

    private static void ApplyMasked(
        object                                         staged,
        object                                         target,
        IReadOnlyDictionary<string, MaskTree.MaskNode> nodes
    ) {
        foreach (var node in nodes.Values) {
            if (!node.Property.CanWrite) continue;

            if (node.IsLeaf) {
                // A whole-field selection replaces the member outright — object references,
                // collections, and record values included.
                node.Property.SetValue(target, node.Property.GetValue(staged));
                continue;
            }

            var stagedValue = node.Property.GetValue(staged);
            if (stagedValue is null) continue;

            var current = node.Property.GetValue(target);
            if (current is null) {
                // A freshly created subtree carries only the selected members, so the projection's
                // unselected members cannot leak onto the target.
                var created = Activator.CreateInstance(node.Property.PropertyType)!;
                ApplyMasked(stagedValue, created, node.Children);
                node.Property.SetValue(target, created);
                continue;
            }

            // A descendant selection moves only the selected members; the target keeps its own
            // instance and every unmasked member.
            ApplyMasked(stagedValue, current, node.Children);
        }
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
}
