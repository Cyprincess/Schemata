using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Issued token or authorization callback returned to an HTTP edge.</summary>
public sealed record AuthorizationSignInResponse(
    TokenResponse?                 Token,
    AuthorizationCallbackResponse? Callback
);