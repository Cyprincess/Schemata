using System;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Common;

namespace Schemata.Mapping.Foundation;

/// <summary>
/// Helper for field-selective mapping that preserves destination values for unmasked fields.
/// </summary>
public static class SimpleMapperHelper
{
    /// <summary>
    /// Maps source to destination using the provided action, but only updates fields in the mask.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="destination">The destination object.</param>
    /// <param name="mask">The field names to update; all other fields retain their pre-map values.</param>
    /// <param name="mapAction">The mapping action to invoke.</param>
    /// <remarks>
    /// Saves the values of non-masked writable properties before mapping, then restores them afterward.
    /// </remarks>
    public static void MapWithMask<TSource, TDestination>(
        TSource                       source,
        TDestination                  destination,
        IEnumerable<string>           mask,
        Action<TSource, TDestination> mapAction
    ) {
        var set        = new HashSet<string>(mask, StringComparer.OrdinalIgnoreCase);
        var properties = AppDomainTypeCache.GetWritableProperties(typeof(TDestination));

        var saved = new (PropertyInfo Prop, object? Value)[properties.Length];
        var count = 0;
        foreach (var property in properties) {
            if (set.Contains(property.Name)) continue;
            saved[count++] = (property, property.GetValue(destination));
        }

        mapAction(source, destination);

        for (var i = 0; i < count; i++) {
            saved[i].Prop.SetValue(destination, saved[i].Value);
        }
    }
}
