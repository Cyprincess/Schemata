using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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

/// <summary>Provisions the selected Identity user type through the shared SCIM protocol.</summary>
/// <typeparam name="TUser">The user entity type selected by <c>UseIdentity</c>.</typeparam>
public sealed class ScimUserHandler<TUser>(
    SchemataUserManager<TUser> users,
    IUserStore<TUser> store,
    IRepository<TUser> repository,
    IScimBindingStore bindings,
    IScimProvisioningDomainProvider domains,
    IScimUserGroupReader<TUser> groupReader
) : IScimResourceHandler
    where TUser : SchemataUser, new()
{

    public ScimResourceType ResourceType => ScimUserMapper.ResourceType;

    public IReadOnlyList<ScimSchema> Schemas => [ScimUserMapper.Schema];

    public async Task<ScimResource?> GetAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
        var user = await FindEntityAsync(id, ct);
        if (user is null) {
            return null;
        }

        var domain = await domains.GetDomainAsync(principal, ct);
        return await MapAsync(user, domain, ct);
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

    private async Task<ScimSearchResult> SearchCoreAsync(
        ScimSearchRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var domain = await domains.GetDomainAsync(principal, ct);
        var entities = await repository.ListAsync(query => query.OrderBy(user => user.Uid), ct).ToListAsync(ct);
        var groups = await LoadGroupsAsync(entities, domain, ct);
        var filter = request.FilterExpression is null ? null : NormalizeFilter(request.FilterExpression);
        var matches = new List<ScimResource>();
        foreach (var entity in entities) {
            var binding = await bindings.FindByEntityUidAsync(ResourceType.Name, entity.Uid, domain, ct);
            var resource = ScimUserMapper.Map(entity, binding?.ExternalId, groups.GetValueOrDefault(entity.Uid) ?? []);
            if (filter is not null) {
                var comparison = resource with { UserName = users.NormalizeName(resource.UserName) ?? resource.UserName };
                var json = ScimProjection.Apply(comparison, Schemas, null, null);
                if (!ScimFilterEvaluator.Matches(filter, json)) {
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
        var input = ScimUserMapper.Read(body);
        var domain = await domains.GetDomainAsync(principal, ct);
        if (input.ExternalId is not null) {
            await EnsureAvailableAsync(input.ExternalId, domain, null, ct);
        }

        var user = new TUser {
            UserName = input.UserName,
            DisplayName = input.DisplayName,
            AdministrativelyActive = input.Active,
        };
        await using var transaction = BeginProvisioning();
        ThrowIfFailed(await users.CreateAsync(user, transaction, ct));
        await SynchronizeBindingAsync(user, input.ExternalId, domain, null, transaction, ct);
        await transaction.CommitAsync(ct);
        return ScimUserMapper.Map(user, input.ExternalId, await GroupsForAsync(user, domain, ct));
    }

    private async Task<ScimResource?> ReplaceCoreAsync(
        string id,
        JsonObject body,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var user = await FindEntityAsync(id, ct);
        if (user is null) {
            return null;
        }

        var input = ScimUserMapper.Read(body);
        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(user, domain, ct);
        return await UpdateAsync(user, input, binding, domain, ct);
    }

    private async Task<ScimResource?> PatchCoreAsync(
        string id,
        ScimPatchDocument patch,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var user = await FindEntityAsync(id, ct);
        if (user is null) {
            return null;
        }

        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(user, domain, ct);
        var current = ScimUserMapper.Map(user, binding?.ExternalId, await GroupsForAsync(user, domain, ct));
        var json = ScimProjection.Apply(current, Schemas, null, null);
        var patched = ScimPatchProcessor.Apply(json, patch, Schemas);
        var input = ScimUserMapper.Read(patched);
        return await UpdateAsync(user, input, binding, domain, ct);
    }

    private async Task<ScimResource> UpdateAsync(
        TUser user,
        ScimUserInput input,
        ScimProvisioningBinding? binding,
        string domain,
        CancellationToken ct
    ) {
        if (input.ExternalId is not null) {
            await EnsureAvailableAsync(input.ExternalId, domain, user.Uid, ct);
        }

        user.UserName = input.UserName;
        user.DisplayName = input.DisplayName;
        await using var transaction = BeginProvisioning();
        var result = user.AdministrativelyActive == input.Active
            ? await users.UpdateAsync(user, transaction, ct)
            : await users.SetAdministrativeActiveAsync(user, input.Active, transaction, ct);
        ThrowIfFailed(result);
        await SynchronizeBindingAsync(user, input.ExternalId, domain, binding, transaction, ct);
        await transaction.CommitAsync(ct);
        return ScimUserMapper.Map(user, input.ExternalId, await GroupsForAsync(user, domain, ct));
    }

    private async Task<bool> DeleteCoreAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
        var user = await FindEntityAsync(id, ct);
        if (user is null) {
            return false;
        }

        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(user, domain, ct);
        await using var transaction = BeginProvisioning();
        ThrowIfFailed(await users.DeleteAsync(user, transaction, ct));
        if (binding is not null) {
            await bindings.RemoveAsync(binding, transaction, ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    private IUnitOfWork BeginProvisioning() {
        if (store is not IUserProvisioningStore<TUser> provisioning) {
            throw new NotSupportedException("The user store does not support transactional provisioning.");
        }

        return provisioning.BeginProvisioning();
    }

    private Task<TUser?> FindEntityAsync(string id, CancellationToken ct) {
        return Guid.TryParse(id, out var uid) ? users.FindByIdAsync(uid.ToString()) : Task.FromResult<TUser?>(null);
    }

    private async Task<ScimProvisioningBinding?> WritableBindingAsync(
        TUser user,
        string domain,
        CancellationToken ct
    ) {
        var entityBindings = await bindings.ListByEntityUidAsync(ResourceType.Name, user.Uid, ct);
        if (entityBindings.Any(binding => !string.Equals(binding.ProvisioningDomain, domain, StringComparison.Ordinal))) {
            throw new ScimProtocolException(403, null, "The user is provisioned in another domain.");
        }

        return entityBindings.SingleOrDefault(binding => string.Equals(binding.ProvisioningDomain, domain, StringComparison.Ordinal));
    }

    private async Task EnsureAvailableAsync(
        string externalId,
        string domain,
        Guid? entityUid,
        CancellationToken ct
    ) {
        var existing = await bindings.FindByExternalIdAsync(ResourceType.Name, domain, externalId, ct);
        if (existing is not null && existing.EntityUid != entityUid) {
            throw new ScimProtocolException(409, ScimConstants.Uniqueness,
                "The externalId is already provisioned in this domain.");
        }
    }

    private async Task SynchronizeBindingAsync(
        TUser user,
        string? externalId,
        string domain,
        ScimProvisioningBinding? binding,
        IUnitOfWork transaction,
        CancellationToken ct
    ) {
        if (string.Equals(binding?.ExternalId, externalId, StringComparison.Ordinal)) {
            return;
        }

        if (binding is not null) {
            await bindings.RemoveAsync(binding, transaction, ct);
        }

        if (externalId is null) {
            return;
        }

        await bindings.StageAsync(new() {
            ResourceType = ResourceType.Name,
            EntityUid = user.Uid,
            ProvisioningDomain = domain,
            ExternalId = externalId,
        }, transaction, ct);
    }

    private async Task<ScimUserResource> MapAsync(TUser user, string domain, CancellationToken ct) {
        var binding = await bindings.FindByEntityUidAsync(ResourceType.Name, user.Uid, domain, ct);
        return ScimUserMapper.Map(user, binding?.ExternalId, await GroupsForAsync(user, domain, ct));
    }

    private async Task<IReadOnlyList<ScimUserGroup>> GroupsForAsync(TUser user, string domain, CancellationToken ct) {
        var projected = await LoadGroupsAsync([user], domain, ct);
        return projected.GetValueOrDefault(user.Uid) ?? [];
    }

    private Task<IReadOnlyDictionary<Guid, IReadOnlyList<ScimUserGroup>>> LoadGroupsAsync(
        IReadOnlyCollection<TUser> entities,
        string domain,
        CancellationToken ct
    ) {
        return groupReader.ListAsync(entities, domain, ct);
    }

    private ScimFilterExpression NormalizeFilter(ScimFilterExpression expression) {
        return expression switch {
            ScimAttributeExpression attribute when string.Equals(attribute.Path.Name, "userName", StringComparison.OrdinalIgnoreCase)
                && attribute.Value?.GetValue<string>() is { } value => attribute with {
                Value = JsonValue.Create(attribute.Operator is ScimComparisonOperator.Co or ScimComparisonOperator.Sw or ScimComparisonOperator.Ew
                    ? Schemata.Identity.Skeleton.Services.UsernameCaseMappedNormalizer.PrepareComparison(value)
                    : users.NormalizeName(value)),
            },
            ScimLogicalExpression logical => logical with {
                Left = NormalizeFilter(logical.Left),
                Right = NormalizeFilter(logical.Right),
            },
            ScimNotExpression negation => negation with { Operand = NormalizeFilter(negation.Operand) },
            _ => expression,
        };
    }

    private static async Task<T> GuardAsync<T>(Func<Task<T>> operation) {
        try {
            return await operation();
        } catch (AlreadyExistsException) {
            throw new ScimProtocolException(409, ScimConstants.Uniqueness, "The user conflicts with an existing resource.");
        } catch (FormatException error) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, error.Message);
        }
    }

    private static void ThrowIfFailed(IdentityResult result) {
        if (result.Succeeded) {
            return;
        }

        if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail")) {
            throw new ScimProtocolException(409, ScimConstants.Uniqueness,
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        throw new ScimProtocolException(400, ScimConstants.InvalidValue,
            string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
