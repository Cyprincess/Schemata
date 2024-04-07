using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Foundation.Models;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;

namespace Schemata.Identity.Foundation.Controllers;

[Authorize]
[Route("~/[controller]")]
[Produces("application/json")]
public class AccountController : ControllerBase
{
    protected readonly IMailSender<SchemataUser>         MailSender;
    protected readonly IMessageSender<SchemataUser>      MessageSender;
    protected readonly SchemataUserManager<SchemataUser> UserManager;

    public AccountController(
        SchemataUserManager<SchemataUser> userManager,
        IMailSender<SchemataUser>         mailSender,
        IMessageSender<SchemataUser>      messageSender) {
        UserManager   = userManager;
        MailSender    = mailSender;
        MessageSender = messageSender;
    }

    [HttpGet(nameof(Profile))]
    public async Task<IActionResult> Profile() {
        if (await UserManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        var store = await UserManager.ToClaimsAsync(user);

        return Ok(store);
    }
}
