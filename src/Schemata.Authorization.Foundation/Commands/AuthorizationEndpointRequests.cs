using Schemata.Abstractions;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record AuthorizeEndpointRequest(
    AuthorizeRequest                 Request,
    ClaimsPrincipal?                 Principal
) : ICommand<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}

public sealed record TokenEndpointRequest(
    TokenRequest                       Request,
    Dictionary<string, List<string?>>? Headers
) : ICommand<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed record RevokeEndpointRequest(
    RevokeRequest                      Request,
    Dictionary<string, List<string?>>? Headers
) : ICommand<Unit>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed record DeviceAuthorizeEndpointRequest(
    DeviceAuthorizeRequest             Request,
    Dictionary<string, List<string?>>? Headers
) : ICommand<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed record EndSessionEndpointRequest(
    EndSessionRequest Request,
    ClaimsPrincipal?   Principal
) : ICommand<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}

public sealed record InteractionApproveRequest(
    InteractRequest   Request,
    ClaimsPrincipal?  Principal,
    string             Issuer
) : ICommand<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}

public sealed record InteractionDenyRequest(
    InteractRequest Request
) : ICommand<Unit>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
