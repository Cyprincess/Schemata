using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Scim.Services;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Stores;
using Schemata.Scim;
using Schemata.Scim.Entities;
using Schemata.Scim.Models;
using Schemata.Scim.Services;

namespace Schemata.Identity.Scim.Handlers;

/// <summary>Provisions roles and direct user memberships through the shared SCIM protocol.</summary>
/// <typeparam name="TUser">The user entity type selected by <c>UseIdentity</c>.</typeparam>
/// <typeparam name="TRole">The role entity type selected by <c>UseIdentity</c>.</typeparam>
public sealed class ScimGroupHandler<TUser, TRole>(
    SchemataRoleManager<TRole> roles,
    SchemataUserManager<TUser> users,
    IRoleStore<TRole> roleStore,
    IRepository<TRole> roleRepository,
    IRepository<TUser> userRepository,
    IScimBindingStore bindings,
    IScimProvisioningDomainProvider domains,
    IHttpContextAccessor context,
    IScimProvisioningAuthorizer? authorizer = null
) : IScimResourceHandler, IScimUserGroupReader<TUser>
    where TUser : SchemataUser, new()
    where TRole : SchemataRole, new()
{
    public ScimResourceType ResourceType => ScimGroupMapper.ResourceType;

    public IReadOnlyList<ScimSchema> Schemas => [ScimGroupMapper.Schema];

    public async Task<ScimResource?> GetAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
        var role = await FindEntityAsync(id, ct);
        if (role is null) {
            return null;
        }

        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await bindings.FindByEntityUidAsync(ResourceType.Name, role.Uid, domain, ct);
        return binding is null ? null : await MapAsync(role, binding, domain, ct);
    }

    public Task<ScimSearchResult> SearchAsync(
        ScimSearchRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        return GuardAsync(() => SearchCoreAsync(request, principal, ct));
    }

    public Task<ScimResource> CreateAsync(JsonObject body, ClaimsPrincipal principal, CancellationToken ct) {
        return GuardAsync(() => CreateCoreAsync(body, principal, ct));
    }

    public Task<ScimResource?> ReplaceAsync(
        string id,
        JsonObject body,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        return GuardAsync(() => ReplaceCoreAsync(id, body, principal, ct));
    }

    public Task<ScimResource?> PatchAsync(
        string id,
        ScimPatchDocument patch,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        return GuardAsync(() => PatchCoreAsync(id, patch, principal, ct));
    }

    public Task<bool> DeleteAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
        return GuardAsync(() => DeleteCoreAsync(id, principal, ct));
    }

    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ScimUserGroup>>> IScimUserGroupReader<TUser>.ListAsync(
        IReadOnlyCollection<TUser> entities,
        string domain,
        CancellationToken ct
    ) {
        var result = entities.ToDictionary(entity => entity.Uid, _ => (IReadOnlyList<ScimUserGroup>)[]);
        if (result.Count == 0) {
            return result;
        }

        var bound = await BoundRolesAsync(domain, ct);
        if (bound.Count == 0) {
            return result;
        }

        foreach (var user in entities) {
            var names = await users.GetRolesAsync(user);
            var groups = bound.Where(pair => pair.Key.DisplayName is not null && names.Contains(pair.Key.DisplayName))
                              .Select(pair => new ScimUserGroup {
                                  Value = pair.Key.Uid.ToString(),
                                  Display = pair.Key.DisplayName,
                                  Ref = ScimResults.ReferenceLocation(
                                      (context.HttpContext ?? throw new InvalidOperationException("SCIM mapping requires an HTTP request.")).Request,
                                      ResourceType.Name, pair.Key.Uid.ToString()),
                                  Type = "direct",
                              })
                              .OrderBy(group => group.Value, StringComparer.Ordinal)
                              .ToList();
            result[user.Uid] = groups;
        }

        return result;
    }

    private async Task<ScimSearchResult> SearchCoreAsync(
        ScimSearchRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var domain = await domains.GetDomainAsync(principal, ct);
        var bound = await BoundRolesAsync(domain, ct);
        var matches = new List<ScimResource>();
        foreach (var (role, binding) in bound) {
            var resource = await MapAsync(role, binding, domain, ct);
            if (request.FilterExpression is not null) {
                var json = ScimProjection.Apply(resource, Schemas, null, null);
                if (!ScimFilterEvaluator.Matches(request.FilterExpression, json)) {
                    continue;
                }
            }

            matches.Add(resource);
        }

        var page = matches.Skip(request.StartIndex - 1).Take(request.Count).Cast<ScimResource>().ToList();
        return new() {
            TotalResults = matches.Count,
            StartIndex = request.StartIndex,
            Resources = page,
        };
    }

    private async Task<ScimResource> CreateCoreAsync(
        JsonObject body,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var input = ScimGroupMapper.Read(body);
        var domain = await domains.GetDomainAsync(principal, ct);
        if (authorizer is not null && !await authorizer.CanWritePermissionsAsync(principal, ct)) {
            throw new ScimProtocolException(403, null, "Role provisioning is not permitted.");
        }

        await EnsureAvailableAsync(input.ExternalId, domain, null, ct);
        var members = await ResolveMembersAsync(input.Members, domain, ct);
        var role = new TRole { DisplayName = input.DisplayName };
        await using var transaction = BeginProvisioning();
        ThrowIfFailed(await roles.CreateAsync(role, transaction, ct));
        await bindings.StageAsync(new() {
            ResourceType = ResourceType.Name,
            EntityUid = role.Uid,
            ProvisioningDomain = domain,
            ExternalId = input.ExternalId,
        }, transaction, ct);
        await SynchronizeMembersAsync(role, members, [], transaction, ct);
        await transaction.CommitAsync(ct);
        return ScimGroupMapper.Map(role, input.ExternalId, members.Select(MapMember).ToList());
    }

    private async Task<ScimResource?> ReplaceCoreAsync(
        string id,
        JsonObject body,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var role = await FindEntityAsync(id, ct);
        if (role is null) {
            return null;
        }

        var input = ScimGroupMapper.Read(body);
        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(role, domain, ct);
        return await UpdateAsync(role, input, binding, domain, ct);
    }

    private async Task<ScimResource?> PatchCoreAsync(
        string id,
        ScimPatchDocument patch,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var role = await FindEntityAsync(id, ct);
        if (role is null) {
            return null;
        }

        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(role, domain, ct);
        var current = await MapAsync(role, binding, domain, ct);
        var json = ScimProjection.Apply(current, Schemas, null, null);
        var patched = ScimPatchProcessor.Apply(json, patch, Schemas);
        var input = ScimGroupMapper.Read(patched);
        return await UpdateAsync(role, input, binding, domain, ct);
    }

    private async Task<ScimResource> UpdateAsync(
        TRole role,
        ScimGroupInput input,
        ScimProvisioningBinding binding,
        string domain,
        CancellationToken ct
    ) {
        await EnsureAvailableAsync(input.ExternalId, domain, role.Uid, ct);
        var desired = await ResolveMembersAsync(input.Members, domain, ct);
        var current = await MembersInRoleAsync(role, ct);
        role.DisplayName = input.DisplayName;
        await using var transaction = BeginProvisioning();
        ThrowIfFailed(await roles.UpdateAsync(role, transaction, ct));
        await SynchronizeBindingAsync(input.ExternalId, binding, transaction, ct);
        await SynchronizeMembersAsync(role, desired, current, transaction, ct);
        await transaction.CommitAsync(ct);
        return ScimGroupMapper.Map(role, input.ExternalId, desired.Select(MapMember).ToList());
    }

    private async Task<bool> DeleteCoreAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
        var role = await FindEntityAsync(id, ct);
        if (role is null) {
            return false;
        }

        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(role, domain, ct);
        await using var transaction = BeginProvisioning();
        ThrowIfFailed(await roles.DeleteAsync(role, transaction, ct));
        await bindings.RemoveAsync(binding, transaction, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private async Task<List<KeyValuePair<TRole, ScimProvisioningBinding>>> BoundRolesAsync(
        string domain,
        CancellationToken ct
    ) {
        var entities = await roleRepository.ListAsync(query => query.OrderBy(role => role.Uid), ct).ToListAsync(ct);
        var result = new List<KeyValuePair<TRole, ScimProvisioningBinding>>();
        foreach (var role in entities) {
            var binding = await bindings.FindByEntityUidAsync(ResourceType.Name, role.Uid, domain, ct);
            if (binding is not null) {
                result.Add(new(role, binding));
            }
        }

        return result;
    }

    private async Task<ScimGroupResource> MapAsync(
        TRole role,
        ScimProvisioningBinding binding,
        string domain,
        CancellationToken ct
    ) {
        var entities = await MembersInRoleAsync(role, ct);
        var members = new List<ScimGroupMember>();
        foreach (var user in entities.OrderBy(user => user.Uid)) {
            if (await bindings.FindByEntityUidAsync(ScimUserMapper.ResourceType.Name, user.Uid, domain, ct) is not null) {
                members.Add(MapMember(user));
            }
        }

        return ScimGroupMapper.Map(role, binding.ExternalId, members);
    }

    private ScimGroupMember MapMember(TUser user) {
        var request = (context.HttpContext ?? throw new InvalidOperationException("SCIM mapping requires an HTTP request.")).Request;
        return ScimGroupMapper.MapMember(user,
            ScimResults.ReferenceLocation(request, ScimUserMapper.ResourceType.Name, user.Uid.ToString()));
    }

    private async Task<List<TUser>> MembersInRoleAsync(TRole role, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        return (await users.GetUsersInRoleAsync(role.DisplayName ?? string.Empty)).ToList();
    }

    private async Task<IReadOnlyList<TUser>> ResolveMembersAsync(
        IReadOnlyList<Guid> ids,
        string domain,
        CancellationToken ct
    ) {
        if (ids.Count == 0) {
            return [];
        }

        var entities = await userRepository.ListAsync(query => query.Where(user => ids.Contains(user.Uid)), ct).ToListAsync(ct);
        var byUid = entities.ToDictionary(user => user.Uid);
        var members = new List<TUser>(ids.Count);
        foreach (var id in ids) {
            if (!byUid.TryGetValue(id, out var user)
                || await bindings.FindByEntityUidAsync(ScimUserMapper.ResourceType.Name, id, domain, ct) is null) {
                throw new ScimProtocolException(400, ScimConstants.InvalidValue,
                    "Every member must be an existing User bound to the group's provisioning domain.");
            }

            members.Add(user);
        }

        return members;
    }

    private async Task SynchronizeMembersAsync(
        TRole role,
        IReadOnlyList<TUser> desired,
        IReadOnlyList<TUser> current,
        IUnitOfWork transaction,
        CancellationToken ct
    ) {
        var keep = desired.Select(user => user.Uid).ToHashSet();
        foreach (var user in current.Where(user => !keep.Contains(user.Uid))) {
            await users.RemoveFromRoleAsync(user, role, transaction, ct);
        }

        var present = current.Select(user => user.Uid).ToHashSet();
        foreach (var user in desired.Where(user => !present.Contains(user.Uid))) {
            await users.AddToRoleAsync(user, role, transaction, ct);
        }
    }

    private async Task<ScimProvisioningBinding> WritableBindingAsync(
        TRole role,
        string domain,
        CancellationToken ct
    ) {
        var entityBindings = await bindings.ListByEntityUidAsync(ResourceType.Name, role.Uid, ct);
        if (entityBindings.Any(binding => !string.Equals(binding.ProvisioningDomain, domain, StringComparison.Ordinal))) {
            throw new ScimProtocolException(403, null, "The group is provisioned in another domain.");
        }

        return entityBindings.SingleOrDefault(binding => string.Equals(binding.ProvisioningDomain, domain, StringComparison.Ordinal))
            ?? throw new ScimProtocolException(403, null, "The role is not bound to this provisioning domain.");
    }

    private async Task EnsureAvailableAsync(
        string? externalId,
        string domain,
        Guid? entityUid,
        CancellationToken ct
    ) {
        if (externalId is null) {
            return;
        }

        var existing = await bindings.FindByExternalIdAsync(ResourceType.Name, domain, externalId, ct);
        if (existing is not null && existing.EntityUid != entityUid) {
            throw new ScimProtocolException(409, ScimConstants.Uniqueness,
                "The externalId is already provisioned in this domain.");
        }
    }

    private async Task SynchronizeBindingAsync(
        string? externalId,
        ScimProvisioningBinding binding,
        IUnitOfWork transaction,
        CancellationToken ct
    ) {
        if (string.Equals(binding.ExternalId, externalId, StringComparison.Ordinal)) {
            return;
        }

        binding.ExternalId = externalId;
        await bindings.UpdateAsync(binding, transaction, ct);
    }

    private IUnitOfWork BeginProvisioning() {
        if (roleStore is not IRoleProvisioningStore<TRole> provisioning) {
            throw new NotSupportedException("The role store does not support transactional provisioning.");
        }

        return provisioning.BeginProvisioning();
    }

    private async Task<TRole?> FindEntityAsync(string id, CancellationToken ct) {
        return Guid.TryParse(id, out var uid) ? await roles.FindByIdAsync(uid.ToString()) : null;
    }

    private static async Task<T> GuardAsync<T>(Func<Task<T>> operation) {
        try {
            return await operation();
        } catch (AlreadyExistsException) {
            throw new ScimProtocolException(409, ScimConstants.Uniqueness, "The group conflicts with an existing resource.");
        } catch (AbortedException error) {
            throw new ScimProtocolException(409, null, error.Message);
        } catch (FormatException error) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, error.Message);
        }
    }

    private static void ThrowIfFailed(IdentityResult result) {
        if (result.Succeeded) {
            return;
        }

        if (result.Errors.Any(error => error.Code == "DuplicateRoleName")) {
            throw new ScimProtocolException(409, ScimConstants.Uniqueness,
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        throw new ScimProtocolException(400, ScimConstants.InvalidValue,
            string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
