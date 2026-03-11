using System;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Common;

namespace Schemata.Mapping.Foundation;

public static class SimpleMapperHelper
{
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
