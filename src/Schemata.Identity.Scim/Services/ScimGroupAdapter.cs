using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Scim;
using Schemata.Scim.Models;

namespace Schemata.Identity.Scim.Services;

/// <summary>
///     The SCIM Group endpoint surface consumed by <see cref="Controllers.ScimGroupsController" />.
/// </summary>
public interface IScimGroupService
{
    /// <summary>Finds a provisioned group by its SCIM id.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The group, or <see langword="null" /> when no provisioned group has that id.</returns>
    Task<ScimGroupResource?> FindAsync(string id, CancellationToken ct = default);

    /// <summary>Lists provisioned groups in id order.</summary>
    /// <param name="startIndex">The 1-based index of the first returned group.</param>
    /// <param name="count">The maximum number of returned groups.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The page and the total number of provisioned groups.</returns>
    Task<ScimGroupPage> ListAsync(int startIndex, int count, CancellationToken ct = default);

    /// <summary>Creates a group and its membership.</summary>
    /// <param name="request">The requested attributes.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The created group.</returns>
    Task<ScimGroupResource> CreateAsync(ScimGroupRequest request, CancellationToken ct = default);

    /// <summary>Replaces a group and its complete membership.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="request">The replacement attributes.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The replaced group, or <see langword="null" /> when no provisioned group has that id.</returns>
    Task<ScimGroupResource?> ReplaceAsync(string id, ScimGroupRequest request, CancellationToken ct = default);

    /// <summary>Deletes a group and its membership.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="false" /> when no provisioned group has that id.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>
///     Reads the provisioned groups a user belongs to for the read-only SCIM User <c>groups</c>
///     attribute. The reader is installed only when the default role services are present.
/// </summary>
/// <typeparam name="TUser">The identity user entity type.</typeparam>
public interface IScimUserGroupReader<TUser>
    where TUser : SchemataUser
{
    /// <summary>Returns the managed groups for each user, keyed by the user's SCIM id.</summary>
    /// <param name="users">The users to resolve.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Group memberships per user; a user without memberships maps to an empty list.</returns>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ScimUserGroup>>> ListAsync(
        IReadOnlyCollection<TUser> users,
        CancellationToken          ct = default
    );
}

/// <summary>
///     The writable SCIM Group attributes. Attributes absent from this type are unsupported and
///     ignored, and read-only attributes supplied by a client are ignored.
/// </summary>
public sealed class ScimGroupRequest
{
    /// <summary>The group display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>The complete requested membership.</summary>
    [JsonPropertyName("members")]
    public IReadOnlyList<ScimGroupMemberRequest>? Members { get; init; }

    /// <summary>Deserializes a request body with the SCIM serializer options.</summary>
    /// <param name="body">The request body.</param>
    /// <returns>The request.</returns>
    public static ScimGroupRequest Parse(JsonElement body) {
        try {
            return body.Deserialize<ScimGroupRequest>(ScimJson.Options) ?? new();
        } catch (JsonException error) {
            throw new ScimGroupException(400, ScimConstants.InvalidSyntax, error.Message);
        }
    }
}

/// <summary>
///     A requested SCIM Group member.
/// </summary>
public sealed class ScimGroupMemberRequest
{
    /// <summary>The member's SCIM id.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; init; }

    /// <summary>The member's absolute location.</summary>
    [JsonPropertyName("$ref")]
    public string? Ref { get; init; }

    /// <summary>The member's resource type; only <c>User</c> is accepted.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

/// <summary>
///     The SCIM Group resource.
/// </summary>
public sealed record ScimGroupResource() : ScimResource([ScimConstants.GroupSchema])
{
    /// <summary>The group display name.</summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>The complete group membership.</summary>
    [JsonPropertyName("members")]
    public IReadOnlyList<ScimGroupMember> Members { get; init; } = [];
}

/// <summary>
///     A SCIM Group member.
/// </summary>
public sealed record ScimGroupMember
{
    /// <summary>The member's SCIM id.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>The member's display name.</summary>
    [JsonPropertyName("display")]
    public string? Display { get; init; }

    /// <summary>The member's absolute location.</summary>
    [JsonPropertyName("$ref")]
    public string? Ref { get; init; }

    /// <summary>The member's resource type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

/// <summary>
///     One page of SCIM Groups.
/// </summary>
/// <param name="TotalResults">The total number of provisioned groups.</param>
/// <param name="StartIndex">The 1-based index of the first returned group.</param>
/// <param name="Resources">The returned groups.</param>
public sealed record ScimGroupPage(int TotalResults, int StartIndex, IReadOnlyList<ScimGroupResource> Resources);

/// <summary>
///     A SCIM error carrying the status and RFC 7644 detail keyword the controller returns.
/// </summary>
public sealed class ScimGroupException(int status, string? scimType, string? detail) : Exception(detail)
{
    /// <summary>The HTTP status code.</summary>
    public int Status { get; } = status;

    /// <summary>The RFC 7644 detail error keyword.</summary>
    public string? ScimType { get; } = scimType;
}

/// <summary>
///     Maps a Schemata role onto the RFC 7643 Group resource and its user-role links onto
///     <c>members</c>. Role writes route through the role mutation owner and membership rows
///     through the user-role repository, so identity admission, security advisors, and the
///     provider transaction stay the single implementation of group writes.
/// </summary>
/// <remarks>
///     Provisioning ownership is recorded as an internal role claim. Roles without that claim are
///     not listed, resolved, or modified through SCIM; an administrator explicitly adopts an
///     existing role by adding the claim. Client input never writes role claims.
/// </remarks>
/// <typeparam name="TUser">The identity user entity type.</typeparam>
public sealed class ScimGroupAdapter<TUser>(
    RoleManager<SchemataRole>                 roles,
    IRepository<SchemataRole>                 roleRepository,
    IRepository<SchemataRoleClaim>            roleClaims,
    IRepository<SchemataUserRole>             userRoles,
    IRepository<TUser>                        users,
    IResourceMutation<SchemataRole>           roleMutation
) : IScimGroupService, IScimUserGroupReader<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>
    ///     The role claim type that marks a role as provisioned through SCIM. An administrator
    ///     adopts an existing role by adding this claim with any value.
    /// </summary>
    public const string ManagedRoleClaimType = SchemataConstants.ProvisioningClaims.ScimGroup;

    /// <summary>The Group resource type served by discovery.</summary>
    public static readonly ScimResourceType ResourceType = new() {
        Id          = "Group",
        Name        = "Group",
        Endpoint    = "/Groups",
        Schema      = ScimConstants.GroupSchema,
        Description = "Group",
    };

    /// <summary>The Group schema served by discovery.</summary>
    public static readonly ScimSchema Schema = new() {
        Id          = ScimConstants.GroupSchema,
        Name        = "Group",
        Description = "Group",
        Attributes = [
            new() {
                Name        = "displayName",
                Type        = ScimConstants.StringType,
                Description = "A human-readable name for the Group.",
                Required    = true,
                CaseExact   = false,
                Mutability  = ScimConstants.ReadWrite,
                Uniqueness  = ScimConstants.UniquenessServer,
            },
            new() {
                Name        = "members",
                Type        = ScimConstants.ComplexType,
                Description = "A list of members of the Group.",
                MultiValued = true,
                Mutability  = ScimConstants.ReadWrite,
                SubAttributes = [
                    new() {
                        Name        = "value",
                        Type        = ScimConstants.StringType,
                        Description = "Identifier of the member of this Group.",
                        Mutability  = ScimConstants.Immutable,
                    },
                    new() {
                        Name           = "$ref",
                        Type           = ScimConstants.ReferenceType,
                        Description    = "The URI corresponding to a SCIM resource that is a member of this Group.",
                        Mutability     = ScimConstants.Immutable,
                        ReferenceTypes = ["User"],
                    },
                    new() {
                        Name            = "type",
                        Type            = ScimConstants.StringType,
                        Description     = "A label indicating the type of resource, e.g., 'User'.",
                        Mutability      = ScimConstants.Immutable,
                        CanonicalValues = ["User"],
                    },
                    new() {
                        Name        = "display",
                        Type        = ScimConstants.StringType,
                        Description = "A human-readable name for the member.",
                        Mutability  = ScimConstants.ReadOnly,
                    },
                ],
            },
        ],
    };

    #region IScimGroupService Members

    /// <inheritdoc />
    public async Task<ScimGroupResource?> FindAsync(string id, CancellationToken ct = default) {
        var role = await FindManagedRoleAsync(id, ct);
        if (role is null) {
            return null;
        }

        var members = await LoadMembersAsync([role.CanonicalName!], ct);

        return Map(role, members.GetValueOrDefault(role.CanonicalName!) ?? []);
    }

    /// <inheritdoc />
    public async Task<ScimGroupPage> ListAsync(int startIndex, int count, CancellationToken ct = default) {
        startIndex = Math.Max(1, startIndex);
        count      = Math.Max(0, count);

        var names = await roleClaims.ListAsync(q => q.Where(rc => rc.ClaimType == ManagedRoleClaimType)
                                                     .Select(rc => rc.RoleId), ct)
                                        .ToListAsync(ct);
        var total = await roleRepository.CountAsync(q => q.Where(r => names.Contains(r.CanonicalName!)), ct);
        if (count == 0 || startIndex - 1 >= total) {
            return new(total, startIndex, []);
        }

        var entities = await roleRepository
                             .ListAsync(q => q.Where(r => names.Contains(r.CanonicalName!))
                                              .OrderBy(r => r.Uid)
                                              .Skip(startIndex - 1)
                                              .Take(count), ct)
                             .ToListAsync(ct);
        var members   = await LoadMembersAsync(entities.Select(r => r.CanonicalName!).ToList(), ct);
        var resources = new List<ScimGroupResource>(entities.Count);
        foreach (var entity in entities) {
            resources.Add(Map(entity, members.GetValueOrDefault(entity.CanonicalName!) ?? []));
        }

        return new(total, startIndex, resources);
    }

    /// <inheritdoc />
    public async Task<ScimGroupResource> CreateAsync(ScimGroupRequest request, CancellationToken ct = default) {
        var role = new SchemataRole {
            DisplayName = RequireDisplayName(request.DisplayName),
        };
        role.NormalizedName = roles.NormalizeKey(role.DisplayName);
        await ValidateAsync(role);

        var members = await ResolveMembersAsync(request.Members, ct);

        await using var transaction = userRoles.Begin();

        var created = await GuardAsync(() => roleMutation.CreateAsync(role, transaction, ct));
        if (created != MutationResult.Applied) {
            throw Forbidden("The group could not be created.");
        }

        await MarkManagedAsync(role, transaction, ct);
        await ReplaceMembersAsync(role, members, [], ct);

        await transaction.CommitAsync(ct);

        return Map(role, members);
    }

    /// <inheritdoc />
    public async Task<ScimGroupResource?> ReplaceAsync(
        string             id,
        ScimGroupRequest   request,
        CancellationToken  ct = default
    ) {
        var role = await FindManagedRoleAsync(id, ct);
        if (role is null) {
            return null;
        }

        role.DisplayName    = RequireDisplayName(request.DisplayName);
        role.NormalizedName = roles.NormalizeKey(role.DisplayName);
        await ValidateAsync(role);

        var members = await ResolveMembersAsync(request.Members, ct);

        await using var transaction = userRoles.Begin();

        var current = await ReadLinksAsync(role.CanonicalName!, ct);

        var updated = await GuardAsync(() => roleMutation.UpdateAsync(role, transaction, Operations.Update, ct));
        if (updated != MutationResult.Applied) {
            throw Forbidden("The group could not be updated.");
        }

        await ReplaceMembersAsync(role, members, current, ct);

        await transaction.CommitAsync(ct);

        return Map(role, members);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
        var role = await FindManagedRoleAsync(id, ct);
        if (role is null) {
            return false;
        }

        var deleted = await GuardAsync(() => roleMutation.DeleteAsync(role, null, Operations.Delete, ct));
        if (deleted != MutationResult.Applied) {
            throw Forbidden("The group could not be deleted.");
        }

        return true;
    }

    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ScimUserGroup>>> IScimUserGroupReader<TUser>.ListAsync(
        IReadOnlyCollection<TUser> users,
        CancellationToken          ct
    ) {
        var result = new Dictionary<Guid, IReadOnlyList<ScimUserGroup>>(users.Count);
        var names  = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var user in users) {
            result[user.Uid] = [];
            if (user.CanonicalName is not null) {
                names[user.CanonicalName] = user.Uid;
            }
        }

        if (names.Count == 0) {
            return result;
        }

        var userIds = names.Keys.ToList();
        var links   = await userRoles.ListAsync(q => q.Where(link => userIds.Contains(link.UserId)), ct)
                                     .ToListAsync(ct);
        if (links.Count == 0) {
            return result;
        }

        var roleNames = links.Select(link => link.RoleId).Distinct().ToList();
        var managed   = await roleClaims.ListAsync(q => q.Where(rc => rc.ClaimType == ManagedRoleClaimType
                                                                   && roleNames.Contains(rc.RoleId))
                                                       .Select(rc => rc.RoleId), ct)
                                       .ToListAsync(ct);
        if (managed.Count == 0) {
            return result;
        }

        var roles = await roleRepository.ListAsync(q => q.Where(r => managed.Contains(r.CanonicalName!)), ct)
                                        .ToListAsync(ct);
        var byCanonical = roles.Where(r => r.CanonicalName is not null)
                               .ToDictionary(r => r.CanonicalName!, StringComparer.Ordinal);

        var groups = new Dictionary<Guid, List<ScimUserGroup>>();
        foreach (var link in links) {
            if (!names.TryGetValue(link.UserId, out var uid)
             || !byCanonical.TryGetValue(link.RoleId, out var role)) {
                continue;
            }

            if (!groups.TryGetValue(uid, out var list)) {
                list        = [];
                groups[uid] = list;
            }

            list.Add(new() { Value = role.Uid.ToString(), Display = role.DisplayName, Type = "direct" });
        }

        foreach (var (uid, list) in groups) {
            list.Sort((left, right) => string.CompareOrdinal(left.Value, right.Value));
            result[uid] = list;
        }

        return result;
    }

    #endregion

    private async Task<SchemataRole?> FindManagedRoleAsync(string id, CancellationToken ct) {
        if (!Guid.TryParse(id, out var uid)) {
            return null;
        }

        var role = await roleRepository.FindAsync([uid], ct);
        if (role?.CanonicalName is null) {
            return null;
        }

        var managed = await roleClaims.AnyAsync(q => q.Where(rc => rc.RoleId == role.CanonicalName
                                                                 && rc.ClaimType == ManagedRoleClaimType), ct);

        return managed ? role : null;
    }

    private async Task<IReadOnlyList<SchemataUserRole>> ReadLinksAsync(string role, CancellationToken ct) {
        return await userRoles.ListAsync(q => q.Where(ur => ur.RoleId == role), ct).ToListAsync(ct);
    }

    private async Task<Dictionary<string, List<ScimGroupMember>>> LoadMembersAsync(
        IReadOnlyCollection<string> roleNames,
        CancellationToken          ct
    ) {
        var result = new Dictionary<string, List<ScimGroupMember>>(StringComparer.Ordinal);
        foreach (var name in roleNames) {
            result[name] = [];
        }

        if (result.Count == 0) {
            return result;
        }

        var names = result.Keys.ToList();
        var links = await userRoles.ListAsync(q => q.Where(ur => names.Contains(ur.RoleId)), ct).ToListAsync(ct);
        if (links.Count == 0) {
            return result;
        }

        var userIds = links.Select(link => link.UserId).Distinct().ToList();
        var entities = await users.ListAsync(q => q.Where(u => userIds.Contains(u.CanonicalName!)), ct)
                                 .ToListAsync(ct);
        var byCanonical = entities.Where(u => u.CanonicalName is not null)
                                  .ToDictionary(u => u.CanonicalName!, StringComparer.Ordinal);

        foreach (var link in links) {
            if (!byCanonical.TryGetValue(link.UserId, out var user)) {
                continue;
            }

            result[link.RoleId].Add(new() {
                Value   = user.Uid.ToString(),
                Display = user.DisplayName ?? user.UserName,
                Type    = "User",
            });
        }

        foreach (var list in result.Values) {
            list.Sort((left, right) => string.CompareOrdinal(left.Value, right.Value));
        }

        return result;
    }

    private async Task<IReadOnlyList<TUser>> ResolveMembersAsync(
        IReadOnlyList<ScimGroupMemberRequest>? requested,
        CancellationToken                      ct
    ) {
        if (requested is null || requested.Count == 0) {
            return [];
        }

        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var member in requested) {
            var id = ValidateMember(member);
            if (seen.Add(id)) {
                ids.Add(id);
            }
        }

        var entities = await users.ListAsync(q => q.Where(u => ids.Contains(u.Uid)), ct).ToListAsync(ct);
        var byUid    = entities.ToDictionary(u => u.Uid);
        var members  = new List<TUser>(ids.Count);
        foreach (var id in ids) {
            if (!byUid.TryGetValue(id, out var user) || user.CanonicalName is null) {
                throw InvalidMember($"User '{id}' was not found.");
            }

            members.Add(user);
        }

        return members;
    }

    private static Guid ValidateMember(ScimGroupMemberRequest member) {
        if (string.IsNullOrWhiteSpace(member.Value) || !Guid.TryParse(member.Value, out var id)) {
            throw InvalidMember("members.value must be a user's SCIM id.");
        }

        if (member.Type is { Length: > 0 } type && !string.Equals(type, "User", StringComparison.OrdinalIgnoreCase)) {
            throw InvalidMember("members.type must be 'User'; nested groups are not supported.");
        }

        if (member.Ref is { Length: > 0 } reference) {
            var path = reference.StartsWith('/')
                           ? reference.Split('?', '#')[0]
                           : Uri.TryCreate(reference, UriKind.Absolute, out var uri)
                               ? uri.AbsolutePath
                               : reference.Split('?', '#')[0];
            if (!path.EndsWith($"/Users/{member.Value}", StringComparison.Ordinal)) {
                throw InvalidMember("members.$ref must address the member's User resource.");
            }
        }

        return id;
    }

    private async Task ReplaceMembersAsync(
        SchemataRole                    role,
        IReadOnlyList<TUser>            members,
        IReadOnlyList<SchemataUserRole> current,
        CancellationToken               ct
    ) {
        var keep = members.Select(user => user.CanonicalName!).ToHashSet(StringComparer.Ordinal);

        foreach (var link in current.Where(link => !keep.Contains(link.UserId))) {
            if (await userRoles.RemoveAsync(link, ct) != MutationResult.Applied) {
                throw Forbidden("The membership change was not permitted.");
            }
        }

        var present = current.Select(link => link.UserId).ToHashSet(StringComparer.Ordinal);
        foreach (var user in members.Where(user => !present.Contains(user.CanonicalName!))) {
            var link = new SchemataUserRole {
                UserId = user.CanonicalName!,
                RoleId = role.CanonicalName!,
            };
            if (await userRoles.AddAsync(link, ct) != MutationResult.Applied) {
                throw Forbidden("The membership change was not permitted.");
            }
        }
    }

    private async Task MarkManagedAsync(SchemataRole role, IUnitOfWork transaction, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(role.CanonicalName)) {
            throw new ScimGroupException(500, null, "The group role has no canonical name.");
        }

        roleClaims.Join(transaction);

        var marker = new SchemataRoleClaim {
            RoleId     = role.CanonicalName,
            ClaimType  = ManagedRoleClaimType,
            ClaimValue = "managed",
        };

        if (await roleClaims.AddAsync(marker, ct) != MutationResult.Applied) {
            throw Forbidden("The group could not be marked as provisioned.");
        }
    }

    private async Task ValidateAsync(SchemataRole role) {
        foreach (var validator in roles.RoleValidators) {
            ThrowIfFailed(await validator.ValidateAsync(roles, role));
        }
    }

    private ScimGroupResource Map(SchemataRole role, IReadOnlyList<TUser> members) {
        var list = new List<ScimGroupMember>(members.Count);
        foreach (var user in members) {
            list.Add(new() {
                Value   = user.Uid.ToString(),
                Display = user.DisplayName ?? user.UserName,
                Type    = "User",
            });
        }

        return Map(role, list);
    }

    private static ScimGroupResource Map(SchemataRole role, IReadOnlyCollection<ScimGroupMember> members) {
        return new() {
            Id          = role.Uid.ToString(),
            DisplayName = role.DisplayName ?? string.Empty,
            Members     = [.. members],
            Meta = new ScimMeta {
                ResourceType = ResourceType.Name,
                Created      = Utc(role.CreateTime),
                LastModified = Utc(role.UpdateTime),
            },
        };
    }

    private static string RequireDisplayName(string? displayName) {
        if (string.IsNullOrWhiteSpace(displayName)) {
            throw new ScimGroupException(400, ScimConstants.InvalidValue, "displayName is required.");
        }

        return displayName;
    }

    private static ScimGroupException InvalidMember(string detail) {
        return new(400, ScimConstants.InvalidValue, detail);
    }

    private static ScimGroupException Forbidden(string detail) {
        return new(403, null, detail);
    }

    private static async Task<MutationResult> GuardAsync(Func<Task<MutationResult>> operation) {
        try {
            return await operation();
        } catch (AlreadyExistsException error) {
            throw new ScimGroupException(409, ScimConstants.Uniqueness, error.Message);
        } catch (AbortedException error) {
            throw new ScimGroupException(409, null, error.Message);
        }
    }

    private static void ThrowIfFailed(IdentityResult result) {
        if (result.Succeeded) {
            return;
        }

        var duplicate = result.Errors.FirstOrDefault(e => e.Code == "DuplicateRoleName");
        if (duplicate is not null) {
            throw new ScimGroupException(409, ScimConstants.Uniqueness, duplicate.Description);
        }

        throw new ScimGroupException(400, ScimConstants.InvalidValue,
                                    string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private static DateTimeOffset? Utc(DateTime? value) {
        return value is null ? null : new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }
}
