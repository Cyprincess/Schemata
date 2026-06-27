using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Schemata.Entity.Repository.Conversions;

namespace Schemata.Entity.EntityFrameworkCore.Conversions;

/// <summary>
///     EF Core <see cref="ValueConverter{TModel, TProvider}" /> that persists
///     <typeparamref name="T" /> as a JSON string column. Delegates the serialization
///     round-trip to <see cref="JsonValueConverter" /> so the EF Core and LINQ to DB
///     bridges write identical bytes.
/// </summary>
/// <remarks>
///     EF Core may clone the converter via reflection when applying model conventions,
///     so the type intentionally exposes a parameterless constructor.
/// </remarks>
/// <typeparam name="T">The CLR property type being persisted as JSON.</typeparam>
public sealed class EfCoreJsonValueConverter<T> : ValueConverter<T, string>
{
    /// <summary>
    ///     Initializes a new <see cref="EfCoreJsonValueConverter{T}" /> using the shared
    ///     JSON helpers in <see cref="JsonValueConverter" />.
    /// </summary>
    public EfCoreJsonValueConverter() : base(
        v => JsonValueConverter.ToProvider(v),
        v => JsonValueConverter.FromProvider<T>(v)!,
        mappingHints: null) { }
}
