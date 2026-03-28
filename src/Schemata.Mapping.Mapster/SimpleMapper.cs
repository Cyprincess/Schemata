using System;
using System.Collections.Generic;
using Mapster;
using MapsterMapper;
using Schemata.Mapping.Foundation;
using Schemata.Mapping.Skeleton;

namespace Schemata.Mapping.Mapster;

/// <summary>
///     Mapster-backed implementation of <see cref="ISimpleMapper" />.
/// </summary>
public sealed class SimpleMapper : ISimpleMapper
{
    private readonly Mapper _mapper;

    /// <summary>
    ///     Initializes a new instance wrapping the given Mapster configuration.
    /// </summary>
    /// <param name="config">The Mapster type adapter configuration.</param>
    public SimpleMapper(TypeAdapterConfig config) { _mapper = new(config); }

    #region ISimpleMapper Members

    /// <inheritdoc />
    public T? Map<T>(object source) { return _mapper.Map<T>(source); }

    /// <inheritdoc />
    public T? Map<T>(object source, Type sourceType, Type destinationType) {
        return (T?)_mapper.Map(source, sourceType, destinationType);
    }

    /// <inheritdoc />
    public TDestination? Map<TSource, TDestination>(TSource source) {
        return _mapper.Map<TSource, TDestination>(source);
    }

    /// <inheritdoc />
    public void Map<TSource, TDestination>(TSource source, TDestination destination) {
        _mapper.Map(source, destination);
    }

    /// <inheritdoc />
    public void Map<TSource, TDestination>(TSource source, TDestination destination, IEnumerable<string> fields) {
        SimpleMapperHelper.MapWithMask(source, destination, fields, (s, d) => _mapper.Map(s, d));
    }

    /// <inheritdoc />
    public object? Map(object source, Type sourceType, Type destinationType) {
        return _mapper.Map(source, sourceType, destinationType);
    }

    /// <inheritdoc />
    public void Map(
        object source,
        object destination,
        Type   sourceType,
        Type   destinationType
    ) {
        _mapper.Map(source, destination, sourceType, destinationType);
    }

    #endregion
}
