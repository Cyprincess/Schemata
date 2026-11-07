using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Scim.Entities;
using Schemata.Scim.Services;

namespace Schemata.Identity.Scim.Integration.Tests.Fixtures;

internal sealed class TestProvisioningDomainProvider : IScimProvisioningDomainProvider
{
    public Task<string> GetDomainAsync(ClaimsPrincipal principal, CancellationToken ct) {
        return Task.FromResult(principal.FindFirst("scim_domain")?.Value ?? "domain-a");
    }
}

internal sealed class DeniedProvisioningAuthorizer : IScimProvisioningAuthorizer
{
    public Task<bool> CanWriteAsync(ClaimsPrincipal principal, string resourceType, string operation, CancellationToken ct) {
        return Task.FromResult(false);
    }

    public Task<bool> CanWritePermissionsAsync(ClaimsPrincipal principal, CancellationToken ct) {
        return Task.FromResult(false);
    }
}

internal sealed class FailingBindingAdvisor : IRepositoryAddAdvisor<ScimProvisioningBinding>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx,
        IRepository<ScimProvisioningBinding> repository,
        ScimProvisioningBinding entity,
        MutationOptions options,
        CancellationToken ct
    ) {
        if (string.Equals(entity.ExternalId, "fail-binding", StringComparison.Ordinal)) {
            throw new InvalidOperationException("The binding write failed.");
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}

internal sealed class PrivilegeDeniedProvisioningAuthorizer : IScimProvisioningAuthorizer
{
    public Task<bool> CanWriteAsync(ClaimsPrincipal principal, string resourceType, string operation, CancellationToken ct) {
        return Task.FromResult(true);
    }

    public Task<bool> CanWritePermissionsAsync(ClaimsPrincipal principal, CancellationToken ct) {
        return Task.FromResult(false);
    }
}

internal sealed class FailingSecondMembershipAdvisor : IRepositoryAddAdvisor<SchemataUserRole>
{
    private int _writes;

    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx,
        IRepository<SchemataUserRole> repository,
        SchemataUserRole entity,
        MutationOptions options,
        CancellationToken ct
    ) {
        _writes++;
        if (_writes == 2) {
            throw new InvalidOperationException("The second membership write failed.");
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
