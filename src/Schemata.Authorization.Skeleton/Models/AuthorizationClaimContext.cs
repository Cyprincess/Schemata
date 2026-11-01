using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Skeleton.Models;

public sealed class AuthorizationClaimContext
{
    public AuthorizationGrantContext? Grant { get; init; }
    public System.Collections.Generic.IReadOnlyCollection<string>? AccessResources { get; init; }
    public ClaimsRequest? RequestedClaims { get; init; }
    public DpopBinding? Dpop { get; init; }
    public TokenResponse? TokenResponse { get; set; }
    public AuthorizationResult? Result { get; set; }
}
