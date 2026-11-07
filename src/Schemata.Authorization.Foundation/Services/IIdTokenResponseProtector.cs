using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Encrypts a completed signed ID token for its registered client.</summary>
public interface IIdTokenResponseProtector
{
    Task<string> ProtectAsync(SchemataApplication application, string signedToken, CancellationToken ct = default);
}
