using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Shared AIP-211 authorization pattern: primary-permission check, parent-read fallback, and
///     permission-denied templating. Used by every per-operation request-authorize advisor to keep the
///     error behavior uniform across the resource pipeline.
/// </summary>
internal static class AuthorizeHelper
{
    /// <summary>
    ///     Standard PERMISSION_DENIED template from AIP-211. Surfaced when the caller can read the parent
    ///     but not perform the operation — the existence of the resource is already observable, so we tell
    ///     them which permission they lack instead of masking it as NOT_FOUND.
    /// </summary>
    public const string PermissionDeniedTemplate = "Permission '{0}' denied on resource '{1}' (or it might not exist).";

    /// <summary>
    ///     Executes the AIP-211 check. If primary access is granted, returns silently. Otherwise performs a
    ///     parent-read probe by re-asking the same access provider with <see cref="Operations.Get" /> —
    ///     this lets implementations distinguish "can see existence" from "cannot see existence" while
    ///     the advisor stays independent of the parent's entity type. Parent pass throws
    ///     <see cref="PermissionDeniedException" /> (visible existence → disclose the missing permission);
    ///     parent fail throws <see cref="NotFoundException" /> (hide existence entirely).
    /// </summary>
    public static async Task EnsureAsync<TEntity, TRequest>(
        IAccessProvider<TEntity, TRequest> access,
        AccessContext<TRequest>            context,
        string                             resource,
        ClaimsPrincipal?                   principal,
        CancellationToken                  ct
    ) {
        if (await access.HasAccessAsync(default, context, principal, ct)) {
            return;
        }

        if (context.Operation == nameof(Operations.Get)) {
            throw SchemataResourceErrors.NotFound<TEntity>(resource);
        }

        var parent = new AccessContext<TRequest> {
            Operation = nameof(Operations.Get),
            Request   = context.Request,
        };

        if (await access.HasAccessAsync(default, parent, principal, ct)) {
            var permission = $"{ResourceNameDescriptor.ForType<TEntity>().Singular.Camelize()}.{context.Operation}";
            throw SchemataResourceErrors.PermissionDenied<TEntity>(
                name: resource,
                description: string.Format(PermissionDeniedTemplate, permission, resource));
        }

        throw SchemataResourceErrors.NotFound<TEntity>(resource);
    }
}
