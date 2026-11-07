using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Skeleton.Services;

/// <summary>Identifies the parent-scoped key slot and storage algorithm for one generated credential.</summary>
/// <param name="Parent">Canonical parent owning the credential.</param>
/// <param name="Usage">Purpose served by the credential.</param>
/// <param name="Algorithm">Stored key algorithm.</param>
public sealed record SecurityKeyGenerationContext(string Parent, string Usage, string Algorithm);

/// <summary>Supplies key material for transactional rotation of one security row type.</summary>
/// <typeparam name="TSecurity">Concrete security entity type.</typeparam>
public interface ISecurityKeyGenerator<TSecurity> where TSecurity : SchemataSecurity
{
    /// <summary>Creates an unpersisted credential for the supplied slot. Rotation assigns its staged lifecycle.</summary>
    /// <param name="context">Parent, usage, and algorithm requested by the caller.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TSecurity> GenerateAsync(SecurityKeyGenerationContext context, CancellationToken ct);
}
