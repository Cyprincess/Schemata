using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Scim;
using Schemata.Scim.Models;

namespace Schemata.Identity.Scim.Services;

/// <summary>The supported SCIM Group representation.</summary>
public sealed record ScimGroupResource() : ScimResource([ScimConstants.GroupSchema])
{
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("members")]
    public IReadOnlyList<ScimGroupMember> Members { get; init; } = [];
}

/// <summary>A direct User membership in a SCIM Group.</summary>
public sealed record ScimGroupMember
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("display")]
    public string? Display { get; init; }

    [JsonPropertyName("$ref")]
    public string? Ref { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

/// <summary>Reads direct Group membership for the read-only SCIM User groups attribute.</summary>
/// <typeparam name="TUser">The Identity user entity type.</typeparam>
public interface IScimUserGroupReader<TUser>
    where TUser : SchemataUser
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ScimUserGroup>>> ListAsync(
        IReadOnlyCollection<TUser> users,
        string domain,
        CancellationToken ct = default
    );
}

internal sealed record ScimGroupInput(string DisplayName, string? ExternalId, IReadOnlyList<Guid> Members);

/// <summary>Maps Identity roles and direct user memberships to the supported SCIM Group schema.</summary>
public static class ScimGroupMapper
{
    public static readonly ScimResourceType ResourceType = new() {
        Id = "Group",
        Name = "Group",
        Endpoint = "/Groups",
        Schema = ScimConstants.GroupSchema,
        Description = "Group",
    };

    public static readonly ScimSchema Schema = new() {
        Id = ScimConstants.GroupSchema,
        Name = "Group",
        Description = "Group",
        Attributes = [
            .. ScimSchemaRegistry.CommonAttributes(),
            new() {
                Name = "displayName",
                Type = ScimConstants.StringType,
                Required = true,
                CaseExact = false,
                Mutability = ScimConstants.ReadWrite,
                Uniqueness = ScimConstants.UniquenessServer,
            },
            new() {
                Name = "members",
                Type = ScimConstants.ComplexType,
                MultiValued = true,
                Mutability = ScimConstants.ReadWrite,
                SubAttributes = [
                    new() { Name = "value", Type = ScimConstants.StringType, Mutability = ScimConstants.Immutable },
                    new() {
                        Name = "$ref",
                        Type = ScimConstants.ReferenceType,
                        CaseExact = true,
                        Mutability = ScimConstants.Immutable,
                        ReferenceTypes = ["User"],
                    },
                    new() {
                        Name = "type",
                        Type = ScimConstants.StringType,
                        Mutability = ScimConstants.Immutable,
                        CanonicalValues = ["User"],
                    },
                    new() { Name = "display", Type = ScimConstants.StringType, Mutability = ScimConstants.ReadOnly },
                ],
            },
        ],
    };

    internal static ScimGroupInput Read(JsonObject body) {
        try {
            var displayName = body["displayName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(displayName)) {
                throw Invalid("displayName is required.");
            }

            var externalId = body["externalId"]?.GetValue<string>();
            if (externalId is not null && string.IsNullOrWhiteSpace(externalId)) {
                throw Invalid("externalId must not be empty.");
            }

            var members = new List<Guid>();
            var seen = new HashSet<Guid>();
            if (body["members"] is null) {
                return new(displayName, externalId, members);
            }

            if (body["members"] is not JsonArray array) {
                throw Invalid("members must be an array.");
            }

            foreach (var node in array) {
                if (node is not JsonObject member) {
                    throw Invalid("Each member must be an object.");
                }

                var value = member["value"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value, out var id)) {
                    throw Invalid("members.value must be a user's SCIM id.");
                }

                var type = member["type"]?.GetValue<string>();
                if (type is not null && !string.Equals(type, "User", StringComparison.OrdinalIgnoreCase)) {
                    throw Invalid("members.type must be 'User'; nested groups are not supported.");
                }

                if (member["$ref"]?.GetValue<string>() is { } reference) {
                    ValidateReference(reference, value);
                }

                if (seen.Add(id)) {
                    members.Add(id);
                }
            }

            return new(displayName, externalId, members);
        } catch (InvalidOperationException error) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, error.Message);
        }
    }

    internal static ScimGroupResource Map<TRole>(
        TRole role,
        string? externalId,
        IReadOnlyList<ScimGroupMember> members
    ) where TRole : SchemataRole {
        return new() {
            Id = role.Uid.ToString(),
            ExternalId = externalId,
            DisplayName = role.DisplayName ?? string.Empty,
            Members = members,
            Meta = new ScimMeta {
                ResourceType = ResourceType.Name,
                Created = Utc(role.CreateTime),
                LastModified = Utc(role.UpdateTime),
            },
        };
    }

    internal static ScimGroupMember MapMember<TUser>(TUser user, string reference) where TUser : SchemataUser {
        return new() {
            Value = user.Uid.ToString(),
            Display = user.DisplayName ?? user.UserName,
            Ref = reference,
            Type = "User",
        };
    }

    private static void ValidateReference(string reference, string value) {
        if (reference.Length == 0 || reference.Contains('?') || reference.Contains('#')) {
            throw Invalid("members.$ref must address the member's User resource.");
        }

        string path;
        if (reference.StartsWith('/')) {
            path = reference;
        } else if (Uri.TryCreate(reference, UriKind.Absolute, out var uri)) {
            path = uri.AbsolutePath;
        } else {
            throw Invalid("members.$ref must be an absolute URI or an absolute path.");
        }

        if (!path.EndsWith($"/Users/{value}", StringComparison.OrdinalIgnoreCase)) {
            throw Invalid("members.$ref must address the member's User resource.");
        }
    }

    private static DateTimeOffset? Utc(DateTime? value) {
        return value is null ? null : new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private static ScimProtocolException Invalid(string detail) {
        return new(400, ScimConstants.InvalidValue, detail);
    }
}
