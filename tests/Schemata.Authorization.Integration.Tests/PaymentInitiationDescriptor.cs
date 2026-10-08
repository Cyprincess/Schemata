using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Schemata.Authorization.Skeleton.Advisors;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     Test <c>payment_initiation</c> descriptor: <c>actions</c>, <c>locations</c>, and
///     <c>institutions</c> are string arrays when present, and a §6.1 narrowing request must
///     name an ordinal subset of each granted array it carries. Omitted fields retain the
///     granted arrays; any requested member beyond the grant is an expansion and yields
///     <c>null</c>.
/// </summary>
internal sealed class PaymentInitiationDescriptor : IAuthorizationDetailTypeDescriptor
{
    private static readonly string[] Fields = ["actions", "locations", "institutions"];

    public string Type => "payment_initiation";

    public string? Validate(JsonElement detail) {
        foreach (var field in Fields) {
            if (!detail.TryGetProperty(field, out var value)) {
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array
             || value.EnumerateArray().Any(member => member.ValueKind != JsonValueKind.String)) {
                return $"The '{field}' member of a payment_initiation detail must be an array of strings.";
            }
        }

        return null;
    }

    public JsonElement? Narrow(JsonElement granted, JsonElement requested) {
        foreach (var field in Fields) {
            if (!requested.TryGetProperty(field, out var wanted)) {
                continue;
            }

            if (!granted.TryGetProperty(field, out var allowed) || allowed.ValueKind != JsonValueKind.Array) {
                return null;
            }

            var allowedValues = allowed.EnumerateArray().Select(member => member.GetString()).ToList();
            if (wanted.EnumerateArray().Any(member => !allowedValues.Contains(member.GetString(), StringComparer.Ordinal))) {
                return null;
            }
        }

        var actual = new JsonObject();
        foreach (var property in requested.EnumerateObject()) {
            actual[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        foreach (var field in Fields) {
            if (actual.ContainsKey(field)
             || !granted.TryGetProperty(field, out var retained)
             || retained.ValueKind != JsonValueKind.Array) {
                continue;
            }

            actual[field] = JsonNode.Parse(retained.GetRawText());
        }

        return JsonSerializer.SerializeToElement(actual);
    }
}