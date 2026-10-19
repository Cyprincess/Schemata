using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

public class IdentityCapabilityShould
{
    [Fact]
    public async Task Omit_Registration_When_Its_Implementation_Is_Not_Installed() {
        using var factory = new WebAppFactory().WithServices(services =>
            services.RemoveAll<IRequestHandler<RegisterUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>>>());
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/Authenticate/Register", new { email = "new@example.com", password = "Password123!" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var login = await client.PostAsJsonAsync("/Authenticate/Login", new { username = "missing@example.com", password = "Password123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
