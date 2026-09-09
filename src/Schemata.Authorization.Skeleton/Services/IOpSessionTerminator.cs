using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>Terminates the host authentication session represented by an OP session.</summary>
public interface IOpSessionTerminator
{
    /// <summary>Signs the user out of the host authentication mechanism.</summary>
    Task TerminateAsync(
        ClaimsPrincipal? principal,
        string?          subject,
        string?          sessionId,
        CancellationToken ct = default
    );
}
