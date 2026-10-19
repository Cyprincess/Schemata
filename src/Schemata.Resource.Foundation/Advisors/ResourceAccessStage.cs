using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Runs the missing-target access check for one entity's standard and custom operations.
///     Installed keyed by <see cref="Type" /> of the entity when authorization is activated for
///     that entity, so callers probe installation with a keyed resolve.
/// </summary>
internal sealed class ResourceAccessStage(IServiceProvider services)
{
    internal Task FinalizeMissingAsync<TEntity, TRequest>(string operation, TRequest request, string? name,
        ClaimsPrincipal? principal, CancellationToken ct) where TEntity : class, ICanonicalName {
        var access = services.GetRequiredService<IAccessProvider<TEntity, TRequest>>();
        return AuthorizeHelper.FinalizeMissingAsync<TEntity, TRequest>(access, operation, request, name, principal, ct);
    }
}
