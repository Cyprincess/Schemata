using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Scim.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Entity.Repository;
using Schemata.Scim;
using Schemata.Scim.Entities;
using Schemata.Scim.Models;
using Schemata.Scim.Services;

namespace Schemata.Authorization.Scim.Handlers;

/// <summary>Provisions OAuth applications through the shared registration manager and metadata mapper.</summary>
public sealed class ScimApplicationHandler<TApp>(
    IApplicationManager<TApp> applications,
    IRepository<TApp> repository,
    IApplicationSecurityMetadataStore security,
    IOptions<SchemataAuthorizationOptions> options,
    IHttpClientFactory http,
    IScimBindingStore bindings,
    IScimProvisioningDomainProvider domains,
    IServiceProvider services,
    IScimProvisioningAuthorizer? authorizer = null
) : IScimResourceHandler
    where TApp : SchemataApplication, new()
{
    public ScimResourceType ResourceType => ScimApplicationSchema.ResourceType;

    public IReadOnlyList<ScimSchema> Schemas => [ScimApplicationSchema.Application, ScimApplicationSchema.Authorization];

    public async Task<ScimResource?> GetAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
        var application = await FindEntityAsync(id, ct);
        if (application is null) {
            return null;
        }
        var domain = await domains.GetDomainAsync(principal, ct);
        return await MapAsync(application, domain, ct);
    }

    public Task<ScimSearchResult> SearchAsync(ScimSearchRequest request, ClaimsPrincipal principal, CancellationToken ct) {
        return GuardAsync(() => SearchCoreAsync(request, principal, ct));
    }

    public Task<ScimResource> CreateAsync(JsonObject body, ClaimsPrincipal principal, CancellationToken ct) {
        return GuardAsync(() => CreateCoreAsync(body, principal, ct));
    }

    public Task<ScimResource?> ReplaceAsync(string id, JsonObject body, ClaimsPrincipal principal, CancellationToken ct) {
        return GuardAsync(() => ReplaceCoreAsync(id, body, principal, ct));
    }

    public Task<ScimResource?> PatchAsync(string id, ScimPatchDocument patch, ClaimsPrincipal principal, CancellationToken ct) {
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
        var entities = await repository.ListAsync(query => query.OrderBy(application => application.Uid), ct).ToListAsync(ct);
        var matches = new List<ScimResource>();
        foreach (var application in entities) {
            var resource = await MapAsync(application, domain, ct);
            if (request.FilterExpression is not null) {
                var json = ScimProjection.Apply(resource, Schemas, null, null);
                if (!ScimFilterEvaluator.Matches(request.FilterExpression, json)) {
                    continue;
                }
            }
            matches.Add(resource);
        }

        return new() {
            TotalResults = matches.Count,
            StartIndex = request.StartIndex,
            Resources = matches.Skip(request.StartIndex - 1).Take(request.Count).Cast<ScimResource>().ToList(),
        };
    }

    private async Task<ScimResource> CreateCoreAsync(JsonObject body, ClaimsPrincipal principal, CancellationToken ct) {
        var input = ScimApplicationMapper.Read(body);
        var domain = await domains.GetDomainAsync(principal, ct);
        if (input.ExternalId is not null) {
            await EnsureAvailableAsync(input.ExternalId, domain, null, ct);
        }
        await AuthorizePermissionsAsync(principal, input, null, ct);

        var application = await RegistrationMetadataMapper.ToApplicationAsync<TApp>(input.Metadata, options, http, ct);
        if (await Advisor.For<IRegistrationRequestAdvisor<TApp>>()
                .RunAsync(new AdviceContext(services), input.Metadata, application, ct) != AdviseResult.Continue) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The Application registration metadata is invalid.");
        }
        if (input.PermissionsSpecified) {
            application.Permissions = input.Permissions;
        }

        await using var transaction = applications.BeginProvisioning();
        var created = await applications.CreateAsync(application, transaction, ct)
            ?? throw new InvalidOperationException("The application was not staged.");
        await security.ReplaceAsync(created, input.Metadata, transaction, ct);
        await SynchronizeBindingAsync(created, input.ExternalId, domain, null, transaction, ct);
        await transaction.CommitAsync(ct);
        return await MapResultAsync(created, input.ExternalId, ct);
    }

    private async Task<ScimResource?> ReplaceCoreAsync(
        string id,
        JsonObject body,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var application = await FindEntityAsync(id, ct);
        if (application is null) {
            return null;
        }
        var input = ScimApplicationMapper.Read(body);
        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(application, domain, ct);
        return await UpdateAsync(application, input, binding, domain, principal, ct);
    }

    private async Task<ScimResource?> PatchCoreAsync(
        string id,
        ScimPatchDocument patch,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        var application = await FindEntityAsync(id, ct);
        if (application is null) {
            return null;
        }
        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(application, domain, ct);
        var current = await MapAsync(application, domain, ct);
        var json = ScimProjection.Apply(current, Schemas, null, null);
        var patched = ScimPatchProcessor.Apply(json, patch, Schemas);
        var input = ScimApplicationMapper.Read(patched);
        return await UpdateAsync(application, input, binding, domain, principal, ct);
    }

    private async Task<ScimResource> UpdateAsync(
        TApp application,
        ScimApplicationInput input,
        ScimProvisioningBinding? binding,
        string domain,
        ClaimsPrincipal principal,
        CancellationToken ct
    ) {
        if (input.ExternalId is not null) {
            await EnsureAvailableAsync(input.ExternalId, domain, application.Uid, ct);
        }
        await AuthorizePermissionsAsync(principal, input, application.Permissions, ct);
        await RegistrationMetadataMapper.ApplyAsync(application, input.Metadata, options, http, ct);
        if (await Advisor.For<IRegistrationRequestAdvisor<TApp>>()
                .RunAsync(new AdviceContext(services), input.Metadata, application, ct) != AdviseResult.Continue) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The Application registration metadata is invalid.");
        }
        if (input.PermissionsSpecified) {
            application.Permissions = input.Permissions;
        }

        await using var transaction = applications.BeginProvisioning();
        await applications.UpdateAsync(application, transaction, ct);
        await security.ReplaceAsync(application, input.Metadata, transaction, ct);
        await SynchronizeBindingAsync(application, input.ExternalId, domain, binding, transaction, ct);
        await transaction.CommitAsync(ct);
        return await MapResultAsync(application, input.ExternalId, ct);
    }

    private async Task<bool> DeleteCoreAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
        var application = await FindEntityAsync(id, ct);
        if (application is null) {
            return false;
        }
        var domain = await domains.GetDomainAsync(principal, ct);
        var binding = await WritableBindingAsync(application, domain, ct);
        await using var transaction = applications.BeginProvisioning();
        await applications.DeleteAsync(application, transaction, ct);
        if (binding is not null) {
            await bindings.RemoveAsync(binding, transaction, ct);
        }
        await transaction.CommitAsync(ct);
        return true;
    }

    private Task<TApp?> FindEntityAsync(string id, CancellationToken ct) {
        if (!Guid.TryParse(id, out var uid)) {
            return Task.FromResult<TApp?>(null);
        }
        return repository.SingleOrDefaultAsync(query => query.Where(application => application.Uid == uid), ct).AsTask();
    }

    private async Task<ScimProvisioningBinding?> WritableBindingAsync(TApp application, string domain, CancellationToken ct) {
        var entityBindings = await bindings.ListByEntityUidAsync(ResourceType.Name, application.Uid, ct);
        if (entityBindings.Any(binding => !string.Equals(binding.ProvisioningDomain, domain, StringComparison.Ordinal))) {
            throw new ScimProtocolException(403, null, "The application is provisioned in another domain.");
        }
        return entityBindings.SingleOrDefault(binding => string.Equals(binding.ProvisioningDomain, domain, StringComparison.Ordinal));
    }

    private async Task EnsureAvailableAsync(string externalId, string domain, Guid? entityUid, CancellationToken ct) {
        var existing = await bindings.FindByExternalIdAsync(ResourceType.Name, domain, externalId, ct);
        if (existing is not null && existing.EntityUid != entityUid) {
            throw new ScimProtocolException(409, ScimConstants.Uniqueness, "The externalId is already provisioned in this domain.");
        }
    }

    private async Task AuthorizePermissionsAsync(
        ClaimsPrincipal principal,
        ScimApplicationInput input,
        IEnumerable<string>? current,
        CancellationToken ct
    ) {
        if (!input.PermissionsSpecified
            || current is null == input.Permissions is null
            && (current ?? []).SequenceEqual(input.Permissions ?? [])) {
            return;
        }
        if (authorizer is null || !await authorizer.CanWritePermissionsAsync(principal, ct)) {
            throw new ScimProtocolException(403, null, "The Application permissions change is not permitted.");
        }
    }

    private async Task SynchronizeBindingAsync(
        TApp application,
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
            if (externalId is null) {
                await bindings.RemoveAsync(binding, transaction, ct);
            } else {
                binding.ExternalId = externalId;
                await bindings.UpdateAsync(binding, transaction, ct);
            }
            return;
        }
        if (externalId is null) {
            return;
        }
        await bindings.StageAsync(new() {
            ResourceType = ResourceType.Name,
            EntityUid = application.Uid,
            ProvisioningDomain = domain,
            ExternalId = externalId,
        }, transaction, ct);
    }

    private async Task<ScimApplicationResource> MapResultAsync(TApp application, string? externalId, CancellationToken ct) {
        var registration = await RegistrationMetadataMapper.ToResponse(application, security, ct);
        if (await Advisor.For<IRegistrationResponseAdvisor<TApp>>()
                .RunAsync(new AdviceContext(services), application, registration, ct) != AdviseResult.Continue) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The Application registration metadata is invalid.");
        }
        return ScimApplicationMapper.Map(application, registration, externalId, application.Permissions?.ToArray());
    }

    private async Task<ScimApplicationResource> MapAsync(TApp application, string domain, CancellationToken ct) {
        var binding = await bindings.FindByEntityUidAsync(ResourceType.Name, application.Uid, domain, ct);
        return await MapResultAsync(application, binding?.ExternalId, ct);
    }

    private static async Task<T> GuardAsync<T>(Func<Task<T>> operation) {
        try {
            return await operation();
        } catch (OAuthException error) {
            throw new ScimProtocolException(error.Code, ScimConstants.InvalidValue, "The Application registration metadata is invalid.");
        } catch (Exception error) when (error is AlreadyExistsException or AbortedException) {
            throw new ScimProtocolException(409, ScimConstants.Uniqueness, "The application conflicts with an existing resource.");
        } catch (FormatException) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The Application representation is invalid.");
        }
    }
}
