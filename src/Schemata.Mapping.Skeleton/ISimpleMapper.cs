using System;

namespace Schemata.Mapping.Skeleton;

public interface ISimpleMapper
{
    T? Map<T>(object source);

    T? Map<T>(object source, Type sourceType);

    TDestination? Map<TSource, TDestination>(TSource source);

    void Map<TSource, TDestination>(TSource source, TDestination destination);

    object? Map(object source, Type destinationType);

    object? Map(object source, Type sourceType, Type destinationType);

    void Map(object source, object destination, Type sourceType, Type destinationType);
}
