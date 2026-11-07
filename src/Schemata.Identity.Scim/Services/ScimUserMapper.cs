using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Scim;
using Schemata.Scim.Models;

namespace Schemata.Identity.Scim.Services;

/// <summary>The minimal SCIM User representation.</summary>
public sealed record ScimUserResource() : ScimResource([ScimConstants.UserSchema])
{
    [System.Text.Json.Serialization.JsonPropertyName("userName")]
    public required string UserName { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("active")]
    public required bool Active { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("groups")]
    public IReadOnlyList<ScimUserGroup> Groups { get; init; } = [];
}

/// <summary>A read-only group membership in a SCIM User representation.</summary>
public sealed record ScimUserGroup
{
    [System.Text.Json.Serialization.JsonPropertyName("value")]
    public required string Value { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("display")]
    public string? Display { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("$ref")]
    public string? Ref { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string? Type { get; init; }
}

internal sealed record ScimUserInput(string UserName, string? DisplayName, bool Active, string? ExternalId);

/// <summary>Maps identity users to the supported SCIM User schema.</summary>
public static class ScimUserMapper
{
    public static readonly ScimResourceType ResourceType = new() {
        Id = "User",
        Name = "User",
        Endpoint = "/Users",
        Schema = ScimConstants.UserSchema,
        Description = "User Account",
    };

    public static readonly ScimSchema Schema = new() {
        Id = ScimConstants.UserSchema,
        Name = "User",
        Description = "User Account",
        Attributes = [
            .. ScimSchemaRegistry.CommonAttributes(),
            new() {
                Name = "userName",
                Type = ScimConstants.StringType,
                Required = true,
                CaseExact = false,
                Mutability = ScimConstants.ReadWrite,
                Uniqueness = ScimConstants.UniquenessServer,
            },
            new() { Name = "displayName", Type = ScimConstants.StringType, Mutability = ScimConstants.ReadWrite },
            new() { Name = "active", Type = ScimConstants.BooleanType, Mutability = ScimConstants.ReadWrite },
            new() {
                Name = "groups",
                Type = ScimConstants.ComplexType,
                MultiValued = true,
                Mutability = ScimConstants.ReadOnly,
                SubAttributes = [
                    new() { Name = "value", Type = ScimConstants.StringType, Mutability = ScimConstants.ReadOnly },
                    new() { Name = "display", Type = ScimConstants.StringType, Mutability = ScimConstants.ReadOnly },
                    new() { Name = "$ref", Type = ScimConstants.ReferenceType, CaseExact = true, Mutability = ScimConstants.ReadOnly, ReferenceTypes = ["Group"] },
                    new() { Name = "type", Type = ScimConstants.StringType, Mutability = ScimConstants.ReadOnly },
                ],
            },
        ],
    };

    internal static ScimUserInput Read(JsonObject body) {
        try {
            var userName = body["userName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(userName)) {
                throw Invalid("userName is required.");
            }

            var externalId = body["externalId"]?.GetValue<string>();
            if (externalId is not null && string.IsNullOrWhiteSpace(externalId)) {
                throw Invalid("externalId must not be empty.");
            }

            return new(
                userName,
                body["displayName"]?.GetValue<string>(),
                body["active"] is { } active ? active.GetValue<bool>() : true,
                externalId);
        } catch (InvalidOperationException error) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, error.Message);
        }
    }

    internal static ScimUserResource Map<TUser>(
        TUser user,
        string? externalId,
        IReadOnlyList<ScimUserGroup> groups
    ) where TUser : SchemataUser {
        return new() {
            Id = user.Uid.ToString(),
            ExternalId = externalId,
            UserName = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Active = user.AdministrativelyActive,
            Groups = groups,
            Meta = new ScimMeta {
                ResourceType = ResourceType.Name,
                Created = Utc(user.CreateTime),
                LastModified = Utc(user.UpdateTime),
            },
        };
    }

    private static DateTimeOffset? Utc(DateTime? value) {
        return value is null ? null : new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private static ScimProtocolException Invalid(string detail) {
        return new(400, ScimConstants.InvalidValue, detail);
    }
}
