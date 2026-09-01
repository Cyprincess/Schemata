using System.Collections.Generic;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Transport-neutral OAuth/OIDC callback parameters.</summary>
public sealed record AuthorizationCallbackResponse(
    string                      RedirectUri,
    Dictionary<string, string?> Parameters,
    string?                     ResponseMode
);