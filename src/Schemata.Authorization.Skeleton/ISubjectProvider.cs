using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Provides claims for a subject and validates subject existence.
///     Used by the UserInfo endpoint and introspection pipeline.
/// </summary>
public interface ISubjectProvider
{
    /// <summary>Returns the claims associated with the given subject identifier.</summary>
    Task<IEnumerable<Claim>> GetClaimsAsync(string subject, CancellationToken ct = default);

    /// <summary>Checks whether the given subject identifier is still valid (not deleted or locked).</summary>
    Task<bool> ValidateAsync(string subject, CancellationToken ct = default);
}
