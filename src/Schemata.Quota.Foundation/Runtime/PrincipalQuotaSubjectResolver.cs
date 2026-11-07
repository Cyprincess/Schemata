using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Quota.Skeleton;

namespace Schemata.Quota.Foundation.Runtime;

public sealed class PrincipalQuotaSubjectResolver : IQuotaSubjectResolver
{
    public ValueTask<QuotaSubject> ResolveAsync(
        ClaimsPrincipal principal,
        string? requestedSubject,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        var claims = principal.Identities.Where(identity => identity.IsAuthenticated).SelectMany(identity => identity.Claims);
        var subject = claims.FirstOrDefault(claim => claim.Type == "sub" && !string.IsNullOrWhiteSpace(claim.Value))?.Value
                   ?? claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier && !string.IsNullOrWhiteSpace(claim.Value))?.Value;
        if (string.IsNullOrWhiteSpace(subject)) throw new UnauthenticatedException();
        if (requestedSubject is not null && !string.Equals(subject, requestedSubject, StringComparison.Ordinal)) {
            throw new PermissionDeniedException();
        }

        return ValueTask.FromResult(new QuotaSubject(subject));
    }
}
