using System;
using System.Collections.Generic;

namespace Schemata.Mapping.Skeleton;

/// <summary>
/// Abstraction layer for object-to-object mapping, decoupling the application from specific mapping libraries.
/// </summary>
/// <remarks>
/// Implementations bridge to concrete mapping engines such as AutoMapper or Mapster.
/// Registered as a scoped service via the mapping feature in <c>Schemata.Mapping.Foundation</c>.
/// </remarks>
public interface ISimpleMapper
{
    /// <summary>
    /// Maps the source object to a new instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The destination type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <returns>The mapped object, or <see langword="null"/> if mapping fails.</returns>
    T? Map<T>(object source);

    /// <summary>
    /// Maps the source object to a new instance of <typeparamref name="T"/> using explicit source and destination types.
    /// </summary>
    /// <typeparam name="T">The cast type for the result.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="sourceType">The runtime source type.</param>
    /// <param name="destinationType">The runtime destination type.</param>
    /// <returns>The mapped object, or <see langword="null"/> if mapping fails.</returns>
    T? Map<T>(object source, Type sourceType, Type destinationType);

    /// <summary>
    /// Maps the source to a new instance of <typeparamref name="TDestination"/>.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <returns>The mapped object, or <see langword="null"/> if mapping fails.</returns>
    TDestination? Map<TSource, TDestination>(TSource source);

    /// <summary>
    /// Maps the source onto an existing destination object.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="destination">The destination object to populate.</param>
    void Map<TSource, TDestination>(TSource source, TDestination destination);

    /// <summary>
    /// Maps only the specified fields from source onto the destination, preserving other destination values.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="source">The source object.</param>
    /// <param name="destination">The destination object to populate.</param>
    /// <param name="fields">The field names to map; all other fields retain their existing values.</param>
    void Map<TSource, TDestination>(TSource source, TDestination destination, IEnumerable<string> fields);

    /// <summary>
    /// Maps the source object to a new instance of the destination type.
    /// </summary>
    /// <param name="source">The source object.</param>
    /// <param name="sourceType">The runtime source type.</param>
    /// <param name="destinationType">The runtime destination type.</param>
    /// <returns>The mapped object, or <see langword="null"/> if mapping fails.</returns>
    object? Map(object source, Type sourceType, Type destinationType);

    /// <summary>
    /// Maps the source onto an existing destination using explicit runtime types.
    /// </summary>
    /// <param name="source">The source object.</param>
    /// <param name="destination">The destination object to populate.</param>
    /// <param name="sourceType">The runtime source type.</param>
    /// <param name="destinationType">The runtime destination type.</param>
    void Map(
        object source,
        object destination,
        Type   sourceType,
        Type   destinationType
    );
}
