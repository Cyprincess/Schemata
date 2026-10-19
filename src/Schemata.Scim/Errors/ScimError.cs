using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Schemata.Scim.Errors;

/// <summary>
///     The RFC 7644 error response body.
/// </summary>
public sealed class ScimError
{
    /// <summary>The error message schema URI.</summary>
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = [ScimConstants.ErrorSchema];

    /// <summary>The HTTP status code as a JSON string.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>The RFC 7644 detail error keyword.</summary>
    [JsonPropertyName("scimType")]
    public string? ScimType { get; init; }

    /// <summary>A human-readable description.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>Creates an error body for the supplied HTTP status code.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="scimType">The RFC 7644 detail error keyword.</param>
    /// <param name="detail">A human-readable description.</param>
    /// <returns>The error body.</returns>
    public static ScimError Create(int status, string? scimType = null, string? detail = null) {
        return new() {
            Status   = status.ToString(CultureInfo.InvariantCulture),
            ScimType = scimType,
            Detail   = detail,
        };
    }
}
