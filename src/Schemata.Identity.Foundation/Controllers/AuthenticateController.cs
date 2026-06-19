using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Controllers;

/// <summary>Exposes identity authentication and account-management endpoints.</summary>
/// <typeparam name="TUser">User entity type handled by the controller.</typeparam>
[ApiController]
[Route("~/Authenticate")]
public sealed partial class AuthenticateController<TUser>(
    IdentityHandler<TUser>              handler,
    IOptionsMonitor<BearerTokenOptions> bearer
) : ControllerBase
    where TUser : SchemataUser, new();
