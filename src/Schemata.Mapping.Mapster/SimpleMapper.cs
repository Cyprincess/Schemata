using System;
using Mapster;
using MapsterMapper;
using Schemata.Mapping.Skeleton;

namespace Schemata.Mapping.Mapster;

public sealed class SimpleMapper : ISimpleMapper
{
    private readonly Mapper _mapper;

    public SimpleMapper(TypeAdapterConfig config) {
        _mapper = new(config);
    }

    #region ISimpleMapper Members

    public T? Map<T>(object source) {
        return _mapper.Map<T>(source);
    }

    public T? Map<T>(object source, Type sourceType, Type destinationType) {
        return (T?)_mapper.Map(source, sourceType, destinationType);
    }

    public TDestination? Map<TSource, TDestination>(TSource source) {
        return _mapper.Map<TSource, TDestination>(source);
    }

    public void Map<TSource, TDestination>(TSource source, TDestination destination) {
        _mapper.Map(source, destination);
    }

    public object? Map(object source, Type sourceType, Type destinationType) {
        return _mapper.Map(source, sourceType, destinationType);
    }

    public void Map(
        object source,
        object destination,
        Type   sourceType,
        Type   destinationType) {
        _mapper.Map(source, destination, sourceType, destinationType);
    }

    #endregion
}
