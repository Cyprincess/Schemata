using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Threading.Tasks;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public class ManagementResourceSecurityShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ManagementResourceSecurityShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task List_Applications_Allows_Anonymous_Request_Without_Authorization() {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/applications/test-client");

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_Application_Returns401_WhenAuthenticationSchemeHasNoCredentials() {
        using var factory = _factory.WithEnvironment("Authenticated").WithServices(ConfigureAuthentication);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/v1/applications/test-client");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Applications_Returns403_WhenAuthorizationClaimDoesNotMatch() {
        using var factory = _factory.WithEnvironment("Authorized").WithServices(ConfigureAuthentication);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(TestAuthenticationHandler.TestScheme);

        var response = await client.GetAsync("/v1/applications");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static void ConfigureAuthentication(IServiceCollection services) {
        services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.TestScheme, _ => { });
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "ManagementTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != TestScheme) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, "management-test-user")], TestScheme));
            return Task.FromResult(AuthenticateResult.Success(new(principal, TestScheme)));
        }
    }
}
