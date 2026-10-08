using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Controllers;

/// <summary>Exposes identity authentication and account-management endpoints.</summary>
/// <typeparam name="TUser">User entity type handled by the controller.</typeparam>
[ApiController]
[Route("~/Authenticate")]
public sealed partial class AuthenticateController<TUser>(
    IRequestDispatcher                        dispatcher,
    IOptionsMonitor<BearerTokenOptions>       bearer,
    IEnumerable<Skeleton.IHostSignInObserver> observers
) : ControllerBase
    where TUser : SchemataUser, new();
