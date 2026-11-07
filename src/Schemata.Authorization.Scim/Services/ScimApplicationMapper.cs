using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Scim;
using Schemata.Scim.Models;

namespace Schemata.Authorization.Scim.Services;

/// <summary>The SCIM Application representation with project-owned metadata attributes.</summary>
public sealed record ScimApplicationResource() : ScimResource([ScimApplicationSchema.ApplicationUri])
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Attributes { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record ScimApplicationInput(
    RegisterRequest Metadata,
    string? ExternalId,
    bool PermissionsSpecified,
    List<string>? Permissions
);

/// <summary>Maps between SCIM camelCase metadata and the native registration contract.</summary>
public static class ScimApplicationMapper
{
    private static readonly JsonSerializerOptions RegistrationJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static ScimApplicationInput Read(JsonObject body) {
        try {
            var metadata = new JsonObject();
            foreach (var (name, value) in body) {
                if (name is "schemas" or "id" or "externalId" or "meta" or "clientId" or "clientIdIssuedAt"
                    || name == ScimApplicationSchema.AuthorizationUri || name.Contains('#')) {
                    continue;
                }

                if (name == "jwks") {
                    if (value is not JsonArray keys) {
                        throw Invalid();
                    }
                    metadata["jwks"] = new JsonObject { ["keys"] = keys.DeepClone() };
                    continue;
                }

                metadata[JsonNamingPolicy.SnakeCaseLower.ConvertName(name)] = value?.DeepClone();
            }

            var request = JsonSerializer.Deserialize<RegisterRequest>(metadata.ToJsonString(), RegistrationJson)
                ?? throw Invalid();
            var externalId = body["externalId"]?.GetValue<string>();
            if (externalId is not null && string.IsNullOrWhiteSpace(externalId)) {
                throw Invalid();
            }

            var specified = body.TryGetPropertyValue(ScimApplicationSchema.AuthorizationUri, out var extension);
            List<string>? permissions = null;
            if (specified && extension is not null) {
                if (extension is not JsonObject authorization) {
                    throw Invalid();
                }
                var node = authorization["permissions"];
                if (node is not null and not JsonArray) {
                    throw Invalid();
                }
                permissions = node is null
                    ? null
                    : ((JsonArray)node).Select(value => value?.GetValue<string>() ?? throw Invalid()).ToList();
                if (permissions?.Any(string.IsNullOrWhiteSpace) == true) {
                    throw Invalid();
                }
            }

            return new(request, externalId, specified, permissions);
        } catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The Application representation is invalid.");
        }
    }

    internal static ScimApplicationResource Map(
        SchemataApplication application,
        RegistrationResponse registration,
        string? externalId,
        IReadOnlyCollection<string>? permissions
    ) {
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var json = JsonSerializer.SerializeToElement(registration, RegistrationJson);
        foreach (var property in json.EnumerateObject()) {
            if (property.Name is "client_secret" or "client_secret_expires_at"
                or "registration_access_token" or "registration_client_uri"
                || property.Name.Contains('#')) {
                continue;
            }

            if (property.Name == "jwks") {
                if (property.Value.TryGetProperty("keys", out var keys)) {
                    attributes["jwks"] = keys.Clone();
                }
                continue;
            }

            attributes[Camel(property.Name)] = property.Value.Clone();
        }

        var schemas = permissions is null
            ? new List<string> { ScimApplicationSchema.ApplicationUri }
            : new List<string> { ScimApplicationSchema.ApplicationUri, ScimApplicationSchema.AuthorizationUri };
        if (permissions is not null) {
            attributes[ScimApplicationSchema.AuthorizationUri] = JsonSerializer.SerializeToElement(new { permissions });
        }

        return new() {
            Schemas = schemas,
            Id = application.Uid.ToString(),
            ExternalId = externalId,
            Attributes = attributes,
            Meta = new() {
                ResourceType = ScimApplicationSchema.ResourceType.Name,
                Created = Utc(application.CreateTime),
                LastModified = Utc(application.UpdateTime),
            },
        };
    }

    private static DateTimeOffset? Utc(DateTime? value) {
        return value is null ? null : new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private static string Camel(string name) {
        var parts = name.Split('_');
        return parts.Length == 1
            ? name
            : parts[0] + string.Concat(parts.Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static ScimProtocolException Invalid() {
        return new(400, ScimConstants.InvalidValue, "The Application representation is invalid.");
    }
}
