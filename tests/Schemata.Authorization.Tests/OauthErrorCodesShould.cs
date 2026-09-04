using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class OauthErrorCodesShould
{
    [Fact]
    public void Register_The_Nine_Oidc_Authentication_Errors() {
        Assert.Equal("interaction_required",        OAuthErrors.InteractionRequired);
        Assert.Equal("account_selection_required",  OAuthErrors.AccountSelectionRequired);
        Assert.Equal("invalid_request_uri",         OAuthErrors.InvalidRequestUri);
        Assert.Equal("invalid_request_object",      OAuthErrors.InvalidRequestObject);
        Assert.Equal("request_not_supported",       OAuthErrors.RequestNotSupported);
        Assert.Equal("request_uri_not_supported",   OAuthErrors.RequestUriNotSupported);
        Assert.Equal("registration_not_supported",  OAuthErrors.RegistrationNotSupported);
        Assert.Equal("invalid_target",              OAuthErrors.InvalidTarget);
    }

    [Fact]
    public async Task Reject_A_Mismatched_Redirect_Uri_With_Invalid_Request() {
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(m => m.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataApplication { ClientId = "client-1", RedirectUris = ["https://rp/cb"] });
        var advisor = new AdviceAuthorizeClientAndRedirect<SchemataApplication>(
            apps.Object, Options.Create(new SchemataAuthorizationOptions()));
        var ctx = new AuthorizeContext<SchemataApplication> {
            Request = new() { ClientId = "client-1", RedirectUri = "https://evil/cb", ResponseType = "code" },
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => advisor.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), ctx));

        // RFC 6749 §4.1.2.1: invalid_redirect_uri is not a registered authorization error; use invalid_request.
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }
}
