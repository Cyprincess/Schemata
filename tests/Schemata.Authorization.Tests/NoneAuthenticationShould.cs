using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Tests;

public class NoneAuthenticationShould
{
    private static SchemataAuthorizationOptions AllowedMethods(params string[] methods) {
        var options = new SchemataAuthorizationOptions();
        options.AllowedClientAuthMethods.Clear();
        foreach (var method in methods) {
            options.AllowedClientAuthMethods.Add(method);
        }

        return options;
    }

    private static Dictionary<string, List<string?>> Form(string? clientId = "public-client") {
        return new() { [Parameters.ClientId] = [clientId] };
    }

    [Fact]
    public void Report_The_None_Method() {
        var authenticator = Create(AllowedMethods(ClientAuthMethods.None), out _, out _);

        Assert.Equal(ClientAuthMethods.None, authenticator.Method);
    }

    [Fact]
    public async Task Claim_A_Identification_Only_Request_For_A_Client_Registered_None() {
        var app = new SchemataApplication {
            ClientId              = "public-client",
            ApplicationType = ApplicationTypes.Native, TokenEndpointAuthMethod = ClientAuthMethods.None,
        };
        var authenticator = Create(AllowedMethods(ClientAuthMethods.None), out var manager, out _, app);

        var authenticated = await authenticator.AuthenticateAsync(null, Form(), null, CancellationToken.None);

        Assert.Same(app, authenticated);
        manager.Verify(m => m.FindByClientIdAsync("public-client", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Decline_A_Identification_Only_Request_For_A_Client_Registered_With_Credentials() {
        // A confidential client must present its credential through its own authenticator;
        // identification alone never downgrades it to none.
        var app = new SchemataApplication {
            ClientId              = "confidential-client",
            
            TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
        };
        var authenticator = Create(AllowedMethods(ClientAuthMethods.None), out _, out _, app);

        var authenticated = await authenticator.AuthenticateAsync(null, Form("confidential-client"), null, CancellationToken.None);

        Assert.Null(authenticated);
    }

    [Fact]
    public async Task Skip_When_No_Client_Id_Is_Present() {
        var authenticator = Create(AllowedMethods(ClientAuthMethods.None), out var manager, out _);

        var authenticated = await authenticator.AuthenticateAsync(null, [], null, CancellationToken.None);

        Assert.Null(authenticated);
        manager.Verify(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Skip_When_The_Server_Does_Not_Allow_The_None_Method() {
        var authenticator = Create(AllowedMethods(ClientAuthMethods.ClientSecretPost), out var manager, out _);

        var authenticated = await authenticator.AuthenticateAsync(null, Form(), null, CancellationToken.None);

        Assert.Null(authenticated);
        manager.Verify(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_An_Unknown_Client_With_Invalid_Client() {
        var authenticator = Create(AllowedMethods(ClientAuthMethods.None), out _, out _, null);

        await Assert.ThrowsAsync<OAuthException>(
            () => authenticator.AuthenticateAsync(null, Form(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Post_Authentication_Leaves_A_Secretless_None_Registered_Client_To_The_None_Authenticator() {
        var app = new SchemataApplication {
            ClientId              = "public-client",
            ApplicationType = ApplicationTypes.Native, TokenEndpointAuthMethod = ClientAuthMethods.None,
        };
        var authenticator = CreatePost(app, out var securities, out var verifier);

        var authenticated = await authenticator.AuthenticateAsync(null, Form(), null, CancellationToken.None);

        // The request is identification-only and the client registered none: client_secret_post
        // must not claim it and report its own method back.
        Assert.Null(authenticated);
        securities.Verify(s => s.ListByParentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        verifier.VerifyNoOtherCalls();
    }

    private static NoneAuthentication<SchemataApplication> Create(
        SchemataAuthorizationOptions                     options,
        out Mock<IApplicationManager<SchemataApplication>> manager,
        out Mock<ISecretVerifier>                        verifier,
        SchemataApplication?                             app = null
    ) {
        manager = new(MockBehavior.Strict);
manager.SetupTypedMetadata();
        manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(app);
        verifier = new();
        return new(manager.Object, Options.Create(options));
    }

    private static ClientSecretPostAuthentication<SchemataApplication> CreatePost(
        SchemataApplication?                             app,
        out Mock<ISecurityStore<SchemataSecurity>>       securities,
        out Mock<ISecretVerifier>                        verifier
    ) {
        var options   = Options.Create(AllowedMethods(ClientAuthMethods.ClientSecretPost, ClientAuthMethods.None));
        var manager   = new Mock<IApplicationManager<SchemataApplication>>(MockBehavior.Strict);
manager.SetupTypedMetadata();
        manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(app);
        securities = new(MockBehavior.Strict);
        verifier   = new();
        return new(manager.Object, options, securities.Object, verifier.Object);
    }
}
