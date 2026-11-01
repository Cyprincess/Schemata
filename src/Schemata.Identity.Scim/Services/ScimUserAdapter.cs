using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Scim;
using Schemata.Scim.Models;

namespace Schemata.Identity.Scim.Services;

/// <summary>
///     The SCIM User endpoint surface consumed by <see cref="Controllers.ScimUsersController" />.
/// </summary>
public interface IScimUserService
{
    /// <summary>Finds a user by its SCIM id.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The user, or <see langword="null" /> when no user has that id.</returns>
    Task<ScimUserResource?> FindAsync(string id, CancellationToken ct = default);

    /// <summary>Lists users in id order.</summary>
    /// <param name="startIndex">The 1-based index of the first returned user.</param>
    /// <param name="count">The maximum number of returned users.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The page and the total number of matching users.</returns>
    Task<ScimUserPage> ListAsync(int startIndex, int count, CancellationToken ct = default);

    /// <summary>Creates a user.</summary>
    /// <param name="request">The requested attributes.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created user.</returns>
    Task<ScimUserResource> CreateAsync(ScimUserRequest request, CancellationToken ct = default);

    /// <summary>Replaces a user.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="request">The replacement attributes.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The replaced user, or <see langword="null" /> when no user has that id.</returns>
    Task<ScimUserResource?> ReplaceAsync(string id, ScimUserRequest request, CancellationToken ct = default);

    /// <summary>Deletes a user.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="false" /> when no user has that id.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>
///     The writable SCIM User attributes. Attributes absent from this type are unsupported and
///     ignored, and read-only attributes supplied by a client are ignored.
/// </summary>
public sealed class ScimUserRequest
{
    /// <summary>The user name.</summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    /// <summary>The display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>Whether the user is administratively enabled.</summary>
    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    /// <summary>The cleartext password accepted on create and replace and never returned.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; init; }

    /// <summary>Deserializes a request body with the SCIM serializer options.</summary>
    /// <param name="body">The request body.</param>
    /// <returns>The request.</returns>
    public static ScimUserRequest Parse(JsonElement body) {
        try {
            return body.Deserialize<ScimUserRequest>(ScimJson.Options) ?? new();
        } catch (JsonException error) {
            throw new ScimUserException(400, ScimConstants.InvalidSyntax, error.Message);
        }
    }
}

/// <summary>
///     The SCIM User resource.
/// </summary>
public sealed record ScimUserResource() : ScimResource([ScimConstants.UserSchema])
{
    /// <summary>The user name.</summary>
    [JsonPropertyName("userName")]
    public required string UserName { get; init; }

    /// <summary>The display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>Whether the user is administratively enabled.</summary>
    [JsonPropertyName("active")]
    public required bool Active { get; init; }

    /// <summary>The managed groups the user belongs to; read-only.</summary>
    [JsonPropertyName("groups")]
    public IReadOnlyList<ScimUserGroup> Groups { get; init; } = [];
}

/// <summary>
///     A read-only SCIM User <c>groups</c> member.
/// </summary>
public sealed record ScimUserGroup
{
    /// <summary>The group's SCIM id.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>The group's display name.</summary>
    [JsonPropertyName("display")]
    public string? Display { get; init; }

    /// <summary>The group's absolute location.</summary>
    [JsonPropertyName("$ref")]
    public string? Ref { get; init; }

    /// <summary>The group type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

/// <summary>
///     One page of SCIM Users.
/// </summary>
/// <param name="TotalResults">The total number of users.</param>
/// <param name="StartIndex">The 1-based index of the first returned user.</param>
/// <param name="Resources">The returned users.</param>
public sealed record ScimUserPage(int TotalResults, int StartIndex, IReadOnlyList<ScimUserResource> Resources);

/// <summary>
///     A SCIM error carrying the status and RFC 7644 detail keyword the controller returns.
/// </summary>
public sealed class ScimUserException(int status, string? scimType, string? detail) : Exception(detail)
{
    /// <summary>The HTTP status code.</summary>
    public int Status { get; } = status;

    /// <summary>The RFC 7644 detail error keyword.</summary>
    public string? ScimType { get; } = scimType;
}

/// <summary>
///     Maps a Schemata identity user onto the RFC 7643 User resource. Writes route through
///     <see cref="SchemataUserManager{TUser}" /> so user-name validation, normalization,
///     uniqueness, password rules, and the repository mutation owners stay the single
///     implementation of identity admission.
/// </summary>
/// <typeparam name="TUser">The identity user entity type.</typeparam>
public sealed class ScimUserAdapter<TUser>(
    SchemataUserManager<TUser>               users,
    IRepository<TUser>                       repository,
    IEnumerable<IScimUserGroupReader<TUser>> groupReaders
) : IScimUserService
    where TUser : SchemataUser, new()
{
    private readonly IScimUserGroupReader<TUser>? _groups = groupReaders.FirstOrDefault();

    /// <summary>The User resource type served by discovery.</summary>
    public static readonly ScimResourceType ResourceType = new() {
        Id          = "User",
        Name        = "User",
        Endpoint    = "/Users",
        Schema      = ScimConstants.UserSchema,
        Description = "User Account",
    };

    /// <summary>The User schema served by discovery.</summary>
    public static readonly ScimSchema Schema = new() {
        Id          = ScimConstants.UserSchema,
        Name        = "User",
        Description = "User Account",
        Attributes = [
            new() {
                Name        = "userName",
                Type        = ScimConstants.StringType,
                Description = "Unique identifier for the User.",
                Required    = true,
                CaseExact   = false,
                Mutability  = ScimConstants.ReadWrite,
                Uniqueness  = ScimConstants.UniquenessServer,
            },
            new() {
                Name        = "displayName",
                Type        = ScimConstants.StringType,
                Description = "The name of the User suitable for display.",
                Mutability  = ScimConstants.ReadWrite,
            },
            new() {
                Name        = "active",
                Type        = ScimConstants.BooleanType,
                Description = "Whether the User is administratively enabled.",
                Mutability  = ScimConstants.ReadWrite,
            },
            new() {
                Name        = "password",
                Type        = ScimConstants.StringType,
                Description = "The User's cleartext password.",
                Mutability  = ScimConstants.WriteOnly,
                Returned    = ScimConstants.Never,
            },
            new() {
                Name        = "groups",
                Type        = ScimConstants.ComplexType,
                Description = "The groups the User belongs to.",
                MultiValued = true,
                Mutability  = ScimConstants.ReadOnly,
                SubAttributes = [
                    new() { Name = "value", Type = ScimConstants.StringType, Description = "The group's SCIM id.", Mutability = ScimConstants.ReadOnly },
                    new() { Name = "display", Type = ScimConstants.StringType, Description = "The group's display name.", Mutability = ScimConstants.ReadOnly },
                    new() { Name = "$ref", Type = ScimConstants.ReferenceType, Description = "The group's absolute location.", Mutability = ScimConstants.ReadOnly, ReferenceTypes = ["Group"] },
                    new() { Name = "type", Type = ScimConstants.StringType, Description = "The group type.", Mutability = ScimConstants.ReadOnly },
                ],
            },
        ],
    };

    #region IScimUserService Members

    /// <inheritdoc />
    public async Task<ScimUserResource?> FindAsync(string id, CancellationToken ct = default) {
        var user = await FindEntityAsync(id);

        return user is null ? null : await MapAsync(user, ct);
    }

    /// <inheritdoc />
    public async Task<ScimUserPage> ListAsync(int startIndex, int count, CancellationToken ct = default) {
        startIndex = Math.Max(1, startIndex);
        count      = Math.Max(0, count);

        var total = await repository.CountAsync<TUser>(null, ct);
        if (count == 0) {
            return new(total, startIndex, []);
        }

        var skip = startIndex - 1;
        if (skip >= total) {
            return new(total, startIndex, []);
        }

        var entities = await repository.ListAsync(q => q.OrderBy(u => u.Uid).Skip(skip).Take(count), ct)
                                        .ToListAsync(ct);
        var groups    = await LoadGroupsAsync(entities, ct);
        var resources = new List<ScimUserResource>(entities.Count);
        foreach (var entity in entities) {
            resources.Add(Map(entity, groups.GetValueOrDefault(entity.Uid) ?? []));
        }

        return new(total, startIndex, resources);
    }

    /// <inheritdoc />
    public async Task<ScimUserResource> CreateAsync(ScimUserRequest request, CancellationToken ct = default) {
        var user = new TUser {
            UserName    = RequireUserName(request.UserName),
            DisplayName = request.DisplayName,
        };
        ApplyActive(user, request.Active ?? true);

        var password = request.Password is null ? null : RequirePassword(request.Password);
        var result = password is null
                         ? await GuardAsync(() => users.CreateAsync(user))
                         : await GuardAsync(() => users.CreateAsync(user, password));
        ThrowIfFailed(result);

        return await MapAsync(user, ct);
    }

    /// <inheritdoc />
    public async Task<ScimUserResource?> ReplaceAsync(
        string             id,
        ScimUserRequest    request,
        CancellationToken  ct = default
    ) {
        var user = await FindEntityAsync(id);
        if (user is null) {
            return null;
        }

        var userName = RequireUserName(request.UserName);
        var active   = IsActive(user);

        if (request.Active is { } requested) {
            ApplyActive(user, requested);
            if (requested != active) {
                user.SecurityStamp = Guid.NewGuid().ToString();
            }
        }

        if (!string.Equals(user.UserName, userName, StringComparison.Ordinal)) {
            user.UserName      = userName;
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        user.DisplayName = request.DisplayName;

        if (request.Password is { } password) {
            await SetPasswordAsync(user, RequirePassword(password));
        }

        ThrowIfFailed(await GuardAsync(() => users.UpdateAsync(user)));

        return await MapAsync(user, ct);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
        var user = await FindEntityAsync(id);
        if (user is null) {
            return false;
        }

        ThrowIfFailed(await GuardAsync(() => users.DeleteAsync(user)));

        return true;
    }

    #endregion

    private async Task<TUser?> FindEntityAsync(string id) {
        return Guid.TryParse(id, out var uid) ? await users.FindByIdAsync(uid.ToString()) : null;
    }

    private async Task<ScimUserResource> MapAsync(TUser user, CancellationToken ct) {
        var groups = await LoadGroupsAsync([user], ct);

        return Map(user, groups.GetValueOrDefault(user.Uid) ?? []);
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ScimUserGroup>>> LoadGroupsAsync(
        IReadOnlyCollection<TUser> users,
        CancellationToken          ct
    ) {
        return _groups is null
                   ? new Dictionary<Guid, IReadOnlyList<ScimUserGroup>>()
                   : await _groups.ListAsync(users, ct);
    }

    private static ScimUserResource Map(TUser user, IReadOnlyList<ScimUserGroup> groups) {
        return new() {
            Id          = user.Uid.ToString(),
            UserName    = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Active      = IsActive(user),
            Groups      = groups,
            Meta = new ScimMeta {
                ResourceType = ResourceType.Name,
                Created      = Utc(user.CreateTime),
                LastModified = Utc(user.UpdateTime),
            },
        };
    }

    private static bool IsActive(TUser user) {
        return user.LockoutEnd != DateTimeOffset.MaxValue;
    }

    private static void ApplyActive(TUser user, bool active) {
        if (active) {
            user.LockoutEnd = null;

            return;
        }

        user.LockoutEnabled = true;
        user.LockoutEnd     = DateTimeOffset.MaxValue;
    }

    private async Task SetPasswordAsync(TUser user, string password) {
        foreach (var validator in users.PasswordValidators) {
            var result = await validator.ValidateAsync(users, user, password);
            ThrowIfFailed(result);
        }

        user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
    }

    private static async Task<IdentityResult> GuardAsync(Func<Task<IdentityResult>> operation) {
        try {
            return await operation();
        } catch (AlreadyExistsException error) {
            throw new ScimUserException(409, ScimConstants.Uniqueness, error.Message);
        }
    }

    private static string RequireUserName(string? userName) {
        if (string.IsNullOrWhiteSpace(userName)) {
            throw new ScimUserException(400, ScimConstants.InvalidValue, "userName is required.");
        }

        return userName;
    }

    private static string RequirePassword(string password) {
        if (string.IsNullOrEmpty(password)) {
            throw new ScimUserException(400, ScimConstants.InvalidValue, "password must not be empty.");
        }

        return password;
    }

    private static void ThrowIfFailed(IdentityResult result) {
        if (result.Succeeded) {
            return;
        }

        var duplicate = result.Errors.FirstOrDefault(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
        if (duplicate is not null) {
            throw new ScimUserException(409, ScimConstants.Uniqueness, duplicate.Description);
        }

        throw new ScimUserException(400, ScimConstants.InvalidValue,
                                    string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private static DateTimeOffset? Utc(DateTime? value) {
        return value is null ? null : new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }
}
