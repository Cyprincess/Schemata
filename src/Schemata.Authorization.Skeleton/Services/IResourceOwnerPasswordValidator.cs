using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>Validates resource owner credentials for the explicitly enabled historical password grant.</summary>
public interface IResourceOwnerPasswordValidator
{
    /// <summary>Returns the admitted user's principal, or <see langword="null" /> when validation rejects the credentials.</summary>
    Task<ClaimsPrincipal?> ValidateAsync(
        SchemataApplication application,
        string username,
        string password,
        CancellationToken ct
    );
}
