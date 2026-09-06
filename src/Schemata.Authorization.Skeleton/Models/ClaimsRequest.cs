using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     The parsed <c>claims</c> authorization request parameter, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#ClaimsParameter">
///         OpenID Connect Core 1.0 §5.5: Requesting Claims using the "claims" Request
///         Parameter
///     </seealso>
///     .
/// </summary>
public sealed class ClaimsRequest
{
    /// <summary>
    ///     Claim names requested from the UserInfo endpoint, mapped to their per-claim
    ///     specification; a <see langword="null" /> value requests the claim in the default
    ///     (voluntary) manner.
    /// </summary>
    public Dictionary<string, ClaimsRequestSpec?>? Userinfo { get; set; }

    /// <summary>
    ///     Claim names requested in the ID Token, mapped to their per-claim specification;
    ///     a <see langword="null" /> value requests the claim in the default (voluntary)
    ///     manner.
    /// </summary>
    public Dictionary<string, ClaimsRequestSpec?>? IdToken { get; set; }

    /// <summary>
    ///     Parses the raw <c>claims</c> parameter value. Returns <see langword="null" /> when
    ///     <paramref name="raw" /> is null or blank. A non-object JSON root is rejected as a
    ///     malformed request per RFC 6749 §3.1.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is neither blank nor a JSON object.</exception>
    public static ClaimsRequest? Parse(string? raw) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return null;
        }

        JsonDocument parsed;
        try {
            parsed = JsonDocument.Parse(raw);
        } catch (JsonException) {
            throw new InvalidOperationException("The claims request parameter is not valid JSON.");
        }

        using var document = parsed;

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("The claims request parameter must be a JSON object.");
        }

        var request = new ClaimsRequest();

        if (root.TryGetProperty("userinfo", out var userinfo) && userinfo.ValueKind == JsonValueKind.Object) {
            request.Userinfo = ParseMembers(userinfo);
        }

        if (root.TryGetProperty("id_token", out var idToken) && idToken.ValueKind == JsonValueKind.Object) {
            request.IdToken = ParseMembers(idToken);
        }

        return request;
    }

    /// <summary>
    ///     Builds a request from the space-joined UserInfo claim names persisted on a token —
    ///     the shape the refresh continuation and the issue pipeline re-publish.
    /// </summary>
    public static ClaimsRequest? FromUserinfoNames(string? names) {
        if (string.IsNullOrWhiteSpace(names)) {
            return null;
        }

        var userinfo = new Dictionary<string, ClaimsRequestSpec?>(StringComparer.Ordinal);
        foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            userinfo[name] = null;
        }

        return userinfo.Count > 0 ? new() { Userinfo = userinfo } : null;
    }

    private static Dictionary<string, ClaimsRequestSpec?> ParseMembers(JsonElement source) {
        var members = new Dictionary<string, ClaimsRequestSpec?>(StringComparer.Ordinal);

        foreach (var property in source.EnumerateObject()) {
            members[property.Name] = property.Value.ValueKind switch {
                JsonValueKind.Null  => null,
                JsonValueKind.Object => ParseSpec(property.Value),
                var _               => throw new InvalidOperationException(
                    $"The claim request for '{property.Name}' must be null or a JSON object."),
            };
        }

        return members;
    }

    private static ClaimsRequestSpec? ParseSpec(JsonElement element) {
        if (element.ValueKind == JsonValueKind.Null) {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("A claim request specification must be null or a JSON object.");
        }

        var spec = new ClaimsRequestSpec();

        if (element.TryGetProperty("essential", out var essential)) {
            spec.Essential = essential.ValueKind switch {
                JsonValueKind.True  => true,
                JsonValueKind.False => false,
                var _               => throw new InvalidOperationException("The essential member must be a boolean."),
            };
        }

        if (element.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String) {
            spec.Value = value.GetString();
        }

        if (element.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array) {
            var list = new List<string>();
            foreach (var item in values.EnumerateArray()) {
                if (item.ValueKind != JsonValueKind.String) {
                    throw new InvalidOperationException("The values member must be an array of strings.");
                }

                list.Add(item.GetString()!);
            }

            spec.Values = list;
        }

        return spec;
    }

}

/// <summary>
///     Per-claim request specification members of the <c>claims</c> parameter
///     (OpenID Connect Core 1.0 §5.5.1): <c>essential</c>, <c>value</c>, and <c>values</c>.
/// </summary>
public sealed class ClaimsRequestSpec
{
    /// <summary>
    ///     Whether the claim is an Essential Claim; <see langword="null" /> means the default
    ///     (voluntary) request.
    /// </summary>
    public bool? Essential { get; set; }

    /// <summary>Requests the claim be returned with this exact value (equality comparison).</summary>
    public string? Value { get; set; }

    /// <summary>Requests the claim be returned with one of these values, in preference order.</summary>
    public List<string>? Values { get; set; }
}
