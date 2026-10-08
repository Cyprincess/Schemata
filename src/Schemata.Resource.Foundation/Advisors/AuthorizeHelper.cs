using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Shared AIP-211 authorization pattern for the per-operation request-authorize advisors. Every
///     definite authorization failure throws PERMISSION_DENIED carrying the resource name, type, and the
///     missing permission; an Indeterminate evaluation fails closed the same way, because the
///     framework default cannot prove the operation is permitted. A probe that re-asks the same
///     entity's access provider with Get is not an AIP-211 parent-resource check and is never
///     performed. NOT_FOUND belongs to lookup misses that occur after authorization succeeds or
///     after a Missing-stage evaluation permits disclosing the absence.
/// </summary>
internal static class AuthorizeHelper
{
    /// <summary>
    ///     Standard PERMISSION_DENIED template from AIP-211, shared with the coarse
    ///     permission-denied path so both stages produce the same error details.
    /// </summary>
    public const string PermissionDeniedTemplate = SchemataResourceErrors.PermissionDeniedTemplate;

    /// <summary>
    ///     Executes the AIP-211 check: silent return when the access check grants the operation,
    ///     PERMISSION_DENIED with the standard template for a definite denial or an
    ///     Indeterminate result.
    /// </summary>
    public static Task EnsureAsync<TEntity, TRequest>(
        IAccessProvider<TEntity, TRequest> access,
        AccessContext<TRequest>            context,
        string                             resource,
        ClaimsPrincipal?                   principal,
        CancellationToken                  ct
    ) {
        return EnsureCoreAsync<TEntity, TRequest>(() => access.HasAccessAsync(default, context, principal, ct), context, resource);
    }

    /// <summary>
    ///     Executes the AIP-211 check against a loaded entity so access providers can make
    ///     instance-aware decisions.
    /// </summary>
    public static Task EnsureAsync<TEntity, TRequest>(
        IAccessProvider<TEntity, TRequest> access,
        TEntity                            entity,
        AccessContext<TRequest>            context,
        string                             resource,
        ClaimsPrincipal?                   principal,
        CancellationToken                  ct
    ) {
        return EnsureCoreAsync<TEntity, TRequest>(() => access.HasAccessAsync(entity, context, principal, ct), context, resource);
    }

    /// <summary>
    ///     Finalizes authorization for a target the load could not produce, following the
    ///     decision matrix: the original target decision is evaluated first — a definite
    ///     <see cref="AccessDecision.Denied" /> terminates with PERMISSION_DENIED and an
    ///     <see cref="AccessDecision.Allowed" /> permits disclosing the absence. Only an
    ///     <see cref="AccessDecision.Indeterminate" /> target consults the
    ///     <see cref="AccessStage.Missing" /> stage, which carries the requested name and the
    ///     actual collection parent (derived from the canonical name) so a provider's real
    ///     parent read-children policy — never a fabricated same-target Get probe — decides
    ///     disclosure. An undecidable parent policy fails closed.
    /// </summary>
    public static async Task FinalizeMissingAsync<TEntity, TRequest>(
        IAccessProvider<TEntity, TRequest> access,
        string?                            operation,
        TRequest?                          request,
        string?                            name,
        ClaimsPrincipal?                   principal,
        CancellationToken                  ct
    )
        where TEntity : class, ICanonicalName {
        var target = new AccessContext<TRequest> {
            Operation = operation,
            Request   = request,
            Stage     = AccessStage.Target,
            Name      = name,
        };

        var decision = await access.HasAccessAsync(default, target, principal, ct);
        if (decision == AccessDecision.Allowed) {
            return;
        }

        if (decision == AccessDecision.Indeterminate) {
            var parent = ResolveParent<TEntity>(name);
            var missing = new AccessContext<TRequest> {
                Operation = operation,
                Request   = request,
                Stage     = AccessStage.Missing,
                Name      = name,
                Parent    = parent,
            };

            if (await access.HasAccessAsync(default, missing, principal, ct) == AccessDecision.Allowed) {
                return;
            }
        }

        var resource = !string.IsNullOrWhiteSpace(name) ? name : typeof(TEntity).Name;
        var permission = $"{ResourceNameDescriptor.ForType<TEntity>().Singular.Camelize()}.{operation}";
        throw SchemataResourceErrors.PermissionDenied<TEntity>(
            resource,
            description: string.Format(PermissionDeniedTemplate, permission, resource));
    }

    /// <summary>
    ///     Derives the actual collection parent from a validated canonical name: a child such as
    ///     <c>tenants/acme/widgets/w1</c> yields <c>tenants/acme</c>; a root resource yields
    ///     <see langword="null" />.
    /// </summary>
    internal static string? ResolveParent<TEntity>(string? name)
        where TEntity : class, ICanonicalName {
        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        if (!descriptor.HasParent || descriptor.ParseCanonicalName(name) is not { } parsed) {
            return null;
        }

        // A canonical name is [parent-literals/]collection/leaf: the parent path is every
        // segment before the final collection literal and leaf.
        var segments = name.Split('/');
        return segments.Length > 2 ? string.Join('/', segments[..^2]) : null;
    }

    private static async Task EnsureCoreAsync<TEntity, TRequest>(
        Func<Task<AccessDecision>>        hasAccess,
        AccessContext<TRequest>           context,
        string                            resource
    ) {
        if (await hasAccess() == AccessDecision.Allowed) {
            return;
        }

        var permission = $"{ResourceNameDescriptor.ForType<TEntity>().Singular.Camelize()}.{context.Operation}";
        throw SchemataResourceErrors.PermissionDenied<TEntity>(
            resource,
            description: string.Format(PermissionDeniedTemplate, permission, resource));
    }
}
