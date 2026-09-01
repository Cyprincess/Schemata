using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Controllers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ConnectControllerRenderingShould
{
    [Fact]
    public async Task Token_Action_Dispatches_And_Renders_Token_Response_As_Json() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("grant"));
        var signIn    = AuthorizationResult.SignIn(principal);
        var token     = new TokenResponse { AccessToken = "access", TokenType = Schemes.Bearer };
        var dispatcher = new Mock<IRequestDispatcher>();
        dispatcher.Setup(value => value.SendAsync<
                             Schemata.Authorization.Foundation.Commands.TokenEndpointRequest,
                             AuthorizationResult>(
                             It.IsAny<Schemata.Authorization.Foundation.Commands.TokenEndpointRequest>(),
                             It.IsAny<CancellationToken>()))
                  .ReturnsAsync(signIn);
        var issuer = new Mock<IAuthorizationSignInService>();
        issuer.Setup(value => value.IssueAsync(
                         principal,
                         It.IsAny<IDictionary<string, string?>?>(),
                         AuthorizationSignInResponseKind.Token,
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AuthorizationSignInResponse(token, null));
        var controller = Controller(dispatcher.Object, issuer.Object);

        var result = await controller.Token(new TokenRequest(), CancellationToken.None);

        Assert.Same(token, Assert.IsType<JsonResult>(result).Value);
        dispatcher.VerifyAll();
    }

    [Fact]
    public async Task Authorize_Action_Dispatches_And_Renders_Callback_Through_Response_Mode_Service() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("authorize"));
        var properties = new Dictionary<string, string?> {
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.RedirectUri]  = "https://client.example/callback",
            [Properties.ResponseMode] = ResponseModes.Query,
            [Properties.State]        = "state-1",
        };
        var signIn = AuthorizationResult.SignIn(principal, properties);
        var dispatcher = new Mock<IRequestDispatcher>();
        dispatcher.Setup(value => value.SendAsync<
                             Schemata.Authorization.Foundation.Commands.AuthorizeEndpointRequest,
                             AuthorizationResult>(
                             It.IsAny<Schemata.Authorization.Foundation.Commands.AuthorizeEndpointRequest>(),
                             It.IsAny<CancellationToken>()))
                  .ReturnsAsync(signIn);
        var issuer = new Mock<IAuthorizationSignInService>();
        issuer.Setup(value => value.IssueAsync(
                         principal,
                         properties,
                         AuthorizationSignInResponseKind.Callback,
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AuthorizationSignInResponse(null, new(
                  "https://client.example/callback",
                  new() { [Parameters.Code] = "code-1", [Parameters.State] = "state-1" },
                  ResponseModes.Query)));
        var controller = Controller(dispatcher.Object, issuer.Object);

        var result = await controller.AuthorizeGet(new AuthorizeRequest(), CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Contains("code=code-1", redirect.Url);
        Assert.Contains("state=state-1", redirect.Url);
        dispatcher.VerifyAll();
    }

    private static ConnectController Controller(
        IRequestDispatcher           dispatcher,
        IAuthorizationSignInService  issuer
    ) {
        return new(
            Options.Create(new SchemataAuthorizationOptions()),
            issuer,
            Options.Create(new JsonSerializerOptions()),
            dispatcher) {
            ControllerContext = new() {
                HttpContext = new DefaultHttpContext {
                    User = new ClaimsPrincipal(new ClaimsIdentity("caller")),
                },
            },
        };
    }
}
