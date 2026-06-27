using System.Text.Json;
using Schemata.Common;

namespace Schemata.Entity.Repository.Conversions;

/// <summary>
///     Shared JSON marshalling for entity property values that ORM bridges persist as
///     a single JSON column. ORM-specific <c>ValueConverter</c> wrappers (EF Core
///     <c>ValueConverter&lt;T, string&gt;</c>, LinqToDB <c>ValueConverter</c>) delegate
///     to <see cref="ToProvider{T}" /> and <see cref="FromProvider{T}" /> so both
///     providers serialize identical bytes and round-trip the same payload.
/// </summary>
/// <remarks>
///     Uses <see cref="SchemataJson.Default" /> so the serialized representation matches
///     other internal persistence paths (flow / job variables, scheduled-operation
///     arguments and results).
/// </remarks>
public static class JsonValueConverter
{
    /// <summary>
    ///     Serializes <paramref name="value" /> to a JSON string. <see langword="null" />
    ///     produces the literal <c>"null"</c> token so a downstream conversion preserves
    ///     the original nullability when reading back.
    /// </summary>
    public static string ToProvider<T>(T? value) {
        return JsonSerializer.Serialize(value, SchemataJson.Default);
    }

    /// <summary>
    ///     Deserializes a JSON string previously produced by <see cref="ToProvider{T}" />.
    ///     An empty or whitespace input returns <see langword="default" />.
    /// </summary>
    public static T? FromProvider<T>(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value, SchemataJson.Default);
    }
}
