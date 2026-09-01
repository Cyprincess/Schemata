using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Identity.Tests;

public class IdentityControllerDispatchShould
{
    [Fact]
    public async Task All_Fourteen_Actions_Dispatch_And_Render_Challenge() {
        var requests   = new List<object>();
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        SetupChallenge<RegisterUserRequest<SchemataUser>, ClaimsPrincipal>(dispatcher, requests);
        SetupChallenge<LoginUserRequest<SchemataUser>, ClaimsPrincipal>(dispatcher, requests);
        SetupChallenge<RefreshUserRequest<SchemataUser>, ClaimsPrincipal>(dispatcher, requests);
        SetupChallenge<GetUserProfileQuery<SchemataUser>, ClaimsStore>(dispatcher, requests);
        SetupChallenge<ChangeUserEmailRequest<SchemataUser>, Unit>(dispatcher, requests);
        SetupChallenge<ChangeUserPhoneRequest<SchemataUser>, Unit>(dispatcher, requests);
        SetupChallenge<ChangeUserPasswordRequest<SchemataUser>, Unit>(dispatcher, requests);
        SetupChallenge<GetUserAuthenticatorRequest<SchemataUser>, AuthenticatorResponse>(dispatcher, requests);
        SetupChallenge<EnrollUserAuthenticatorRequest<SchemataUser>, Unit>(dispatcher, requests);
        SetupChallenge<DowngradeUserAuthenticatorRequest<SchemataUser>, Unit>(dispatcher, requests);
        SetupChallenge<ForgotUserPasswordRequest<SchemataUser>, Unit>(dispatcher, requests);
        SetupChallenge<ResetUserPasswordRequest<SchemataUser>, Unit>(dispatcher, requests);
        SetupChallenge<ConfirmUserRequest<SchemataUser>, Unit>(dispatcher, requests);
        SetupChallenge<SendUserConfirmationCodeRequest<SchemataUser>, Unit>(dispatcher, requests);
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var ticket    = new AuthenticationTicket(principal, "refresh");
        var protector = new Mock<ISecureDataFormat<AuthenticationTicket>>();
        protector.Setup(value => value.Unprotect("refresh-token")).Returns(ticket);
        var bearer = new Mock<IOptionsMonitor<BearerTokenOptions>>();
        bearer.Setup(value => value.Get(IdentityConstants.BearerScheme))
              .Returns(new BearerTokenOptions { RefreshTokenProtector = protector.Object });
        var controller = new AuthenticateController<SchemataUser>(dispatcher.Object, bearer.Object) {
            ControllerContext = new() {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };
        var register     = new RegisterRequest();
        var login        = new LoginRequest();
        var profile      = new ProfileRequest();
        var authenticator = new AuthenticatorRequest();
        var forgot       = new ForgetRequest();
        var reset        = new ResetRequest();
        var confirm      = new ConfirmRequest();

        var results = new[] {
            await controller.Register(register, CancellationToken.None),
            await controller.Login(login, CancellationToken.None),
            await controller.Refresh(new RefreshRequest { RefreshToken = "refresh-token" }, CancellationToken.None),
            await controller.Profile(CancellationToken.None),
            await controller.Email(profile, CancellationToken.None),
            await controller.Phone(profile, CancellationToken.None),
            await controller.Password(profile, CancellationToken.None),
            await controller.Authenticator(CancellationToken.None),
            await controller.Enroll(authenticator, CancellationToken.None),
            await controller.Downgrade(authenticator, CancellationToken.None),
            await controller.Forgot(forgot, CancellationToken.None),
            await controller.Reset(reset, CancellationToken.None),
            await controller.Confirm(confirm, CancellationToken.None),
            await controller.Code(forgot, CancellationToken.None),
        };

        Assert.All(results, result => Assert.IsType<ChallengeResult>(result));
        Assert.Collection(
            requests,
            request => Assert.Same(register, Assert.IsType<RegisterUserRequest<SchemataUser>>(request).Request),
            request => Assert.Same(login, Assert.IsType<LoginUserRequest<SchemataUser>>(request).Request),
            request => Assert.Same(ticket, Assert.IsType<RefreshUserRequest<SchemataUser>>(request).Ticket),
            request => Assert.IsType<GetUserProfileQuery<SchemataUser>>(request),
            request => Assert.Same(profile, Assert.IsType<ChangeUserEmailRequest<SchemataUser>>(request).Request),
            request => Assert.Same(profile, Assert.IsType<ChangeUserPhoneRequest<SchemataUser>>(request).Request),
            request => Assert.Same(profile, Assert.IsType<ChangeUserPasswordRequest<SchemataUser>>(request).Request),
            request => Assert.IsType<GetUserAuthenticatorRequest<SchemataUser>>(request),
            request => Assert.Same(authenticator, Assert.IsType<EnrollUserAuthenticatorRequest<SchemataUser>>(request).Request),
            request => Assert.Same(authenticator, Assert.IsType<DowngradeUserAuthenticatorRequest<SchemataUser>>(request).Request),
            request => Assert.Same(forgot, Assert.IsType<ForgotUserPasswordRequest<SchemataUser>>(request).Request),
            request => Assert.Same(reset, Assert.IsType<ResetUserPasswordRequest<SchemataUser>>(request).Request),
            request => Assert.Same(confirm, Assert.IsType<ConfirmUserRequest<SchemataUser>>(request).Request),
            request => Assert.Same(forgot, Assert.IsType<SendUserConfirmationCodeRequest<SchemataUser>>(request).Request));
        Assert.All(requests, request => Assert.Same(principal, Assert.IsAssignableFrom<IRequestPrincipal>(request).Principal));
    }

    private static void SetupChallenge<TRequest, TPayload>(
        Mock<IRequestDispatcher> dispatcher,
        List<object>             requests
    ) where TRequest : IRequest<IdentityResult<TPayload>> {
        dispatcher.Setup(value => value.SendAsync<TRequest, IdentityResult<TPayload>>(
                             It.IsAny<TRequest>(), It.IsAny<CancellationToken>()))
                  .Callback((TRequest request, CancellationToken _) => requests.Add(request!))
                  .ReturnsAsync(IdentityResult<TPayload>.Challenge());
    }
}
