using Schemata.Identity.Integration.Tests.Fixtures;
using System.Net;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

public class ManagementResourceSecurityShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ManagementResourceSecurityShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Get_User_Allows_Anonymous_Request_Without_Authorization() {
        var response = await _factory.CreateClient().GetAsync("/v1/users/test-user");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_User_Returns401_WhenAuthenticationSchemeHasNoCredentials() {
        using var factory = _factory.WithAuthentication().WithServices(ConfigureAuthentication);
        var response = await factory.CreateClient().GetAsync("/v1/users/test-user");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void ConfigureAuthentication(IServiceCollection services) {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.TestScheme, _ => { });
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "ManagementTest";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
