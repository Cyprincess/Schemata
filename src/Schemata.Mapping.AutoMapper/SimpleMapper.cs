using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Options;
using Schemata.Mapping.Skeleton;

namespace Schemata.Mapping.AutoMapper;

public sealed class SimpleMapper : ISimpleMapper
{
    private readonly IMapper _mapper;

    public SimpleMapper(IOptions<SchemataMappingOptions> options) {
        var config = new MapperConfiguration(mapper => { AutoMapperConfigurator.Configure(mapper, options.Value); });

        _mapper = new Mapper(config);
    }

    #region ISimpleMapper Members

    public IEnumerable<T?> Map<T>(IEnumerable<object> source) {
        return _mapper.Map<IEnumerable<T>>(source);
    }

    public async IAsyncEnumerable<T?> MapAsync<T>(
        IAsyncEnumerable<object>                   source,
        [EnumeratorCancellation] CancellationToken ct = default) {
        await foreach (var item in source.WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            yield return _mapper.Map<T>(item);
        }
    }

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
