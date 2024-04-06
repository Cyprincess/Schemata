using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Controllers;

[Route("~/[controller]")]
[Produces("application/json")]
public partial class AuthenticateController : ControllerBase
{
    private readonly SignInManager<SchemataUser>         _sign;
    private readonly UserManager<SchemataUser>           _users;
    private readonly IOptionsMonitor<BearerTokenOptions> _options;

    public AuthenticateController(
        SignInManager<SchemataUser>         sign,
        UserManager<SchemataUser>           users,
        IOptionsMonitor<BearerTokenOptions> options) {
        _sign         = sign;
        _users        = users;
        _options = options;
    }
}
