using LinqToDB.Mapping;
using Schemata.Entity.Repository.Conversions;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     LINQ to DB value converter that persists <typeparamref name="T" /> as a JSON string
///     column. Delegates the serialization round-trip to <see cref="JsonValueConverter" />
///     so both ORM bridges write identical bytes.
/// </summary>
/// <typeparam name="T">The CLR property type being persisted as JSON.</typeparam>
public sealed class LinqToDbJsonConverter<T> : ValueConverter<T?, string?>
{
    /// <summary>
    ///     Initializes a new <see cref="LinqToDbJsonConverter{T}" />. The metadata reader
    ///     instantiates this type via <see cref="ValueConverterAttribute.ConverterType" />
    ///     when it discovers a property whose declared type is a supported collection or
    ///     dictionary shape.
    /// </summary>
    public LinqToDbJsonConverter() : base(
        v => JsonValueConverter.ToProvider(v),
        v => JsonValueConverter.FromProvider<T>(v),
        false) { }
}
