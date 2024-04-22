using System;
using System.Collections.Generic;
using System.Threading;

namespace Schemata.Mapping.Skeleton;

public interface ISimpleMapper
{
    IEnumerable<T?> Map<T>(IEnumerable<object> source);

    IAsyncEnumerable<T?> MapAsync<T>(IAsyncEnumerable<object> source, CancellationToken ct = default);

    T? Map<T>(object source);

    T? Map<T>(object source, Type sourceType, Type destinationType);

    TDestination? Map<TSource, TDestination>(TSource source);

    void Map<TSource, TDestination>(TSource source, TDestination destination);

    object? Map(object source, Type sourceType, Type destinationType);

    void Map(object source, object destination, Type sourceType, Type destinationType);
}
