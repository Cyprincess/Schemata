using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    ///     The <c>acr</c> values requested as an Essential Claim for the ID Token
    ///     (OpenID Connect Core 1.0 §5.5.1), in preference order; <see langword="null" /> when the
    ///     request does not make <c>acr</c> essential.
    /// </summary>
    public List<string>? EssentialAcrValues() {
        if (IdToken?.GetValueOrDefault(AuthorizationConstants.Claims.Acr) is not { Essential: true } acr) {
            return null;
        }

        if (acr.Values is { Count: > 0 } values) {
            return values;
        }

        return acr.Value is { Length: > 0 } single ? [single] : null;
    }

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
    ///     Builds a request from the serialized UserInfo claim request persisted on a token —
    ///     the shape the refresh continuation and the issue pipeline re-publish.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is neither blank nor a JSON object.</exception>
    public static ClaimsRequest? FromUserinfoRequest(string? raw) {
        var userinfo = ParseUserinfo(raw);
        return userinfo is { Count: > 0 } ? new() { Userinfo = userinfo } : null;
    }

    /// <summary>
    ///     Parses the persisted UserInfo claim request object: claim names mapped to their
    ///     per-claim specification, so the <c>value</c>/<c>values</c> qualifiers survive onto
    ///     the token. Returns <see langword="null" /> when <paramref name="raw" /> is null or
    ///     blank.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is neither blank nor a JSON object.</exception>
    public static Dictionary<string, ClaimsRequestSpec?>? ParseUserinfo(string? raw) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return null;
        }

        JsonDocument parsed;
        try {
            parsed = JsonDocument.Parse(raw);
        } catch (JsonException) {
            throw new InvalidOperationException("The persisted userinfo claim request is not valid JSON.");
        }

        using var document = parsed;
        if (document.RootElement.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("The persisted userinfo claim request must be a JSON object.");
        }

        return ParseMembers(document.RootElement);
    }

    /// <summary>Serializes a UserInfo claim request object for persistence on a token.</summary>
    public static string SerializeUserinfo(IReadOnlyDictionary<string, ClaimsRequestSpec?> userinfo) {
        var root = new JsonObject();
        foreach (var (name, spec) in userinfo) {
            root[name] = spec is null ? null : SerializeSpec(spec);
        }

        return root.ToJsonString();
    }

    private static JsonObject SerializeSpec(ClaimsRequestSpec spec) {
        var node = new JsonObject();
        if (spec.Essential is { } essential) {
            node["essential"] = essential;
        }

        if (spec.Value is { } value) {
            node["value"] = value;
        }

        if (spec.Values is { } values) {
            node["values"] = new JsonArray(values.Select(item => JsonValue.Create(item)).ToArray());
        }

        return node;
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