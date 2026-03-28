using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton;

public interface ISubjectProvider
{
    Task<IEnumerable<Claim>> GetClaimsAsync(string subject, CancellationToken ct = default);

    Task<bool> ValidateAsync(string subject, CancellationToken ct = default);
}
