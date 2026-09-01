using System;
using System.Security.Claims;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Security.Skeleton.Advisors;

internal static class PermissionProbe
{
    public static Exception Create(
        string              operation,
        Type                entity,
        IPermissionResolver resolver,
        IPermissionMatcher  matcher,
        ClaimsPrincipal?    principal
    ) {
        if (operation == nameof(Operations.Get)) {
            return new NotFoundException();
        }

        if (operation is nameof(Operations.Update) or nameof(Operations.Delete)) {
            var permission = resolver.Resolve(nameof(Operations.Get), entity);
            return principal is not null && matcher.IsMatch(principal, permission)
                ? new PermissionDeniedException()
                : new NotFoundException();
        }

        return new PermissionDeniedException();
    }
}
