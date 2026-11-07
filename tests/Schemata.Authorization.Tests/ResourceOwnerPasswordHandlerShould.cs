using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Identity.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Services;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Skeleton.Stores;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

[Trait("Layer", "Unit")]
public class ResourceOwnerPasswordHandlerShould
{
    private const string Password = "user-password-DO-NOT-LEAK";

    [Fact]
    public async Task Authenticated_Client_And_User_Receive_Bearer_Sign_In() {
        var principal = Principal();
        var host = CreateHandler(principal);
        var request = Request();

        var result = await host.handler.HandleAsync(request, null, null, new AdviceContext(host.services), CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        var identity = Assert.Single(result.Principal!.Identities);
        Assert.Equal(SchemataAuthorizationSchemes.Bearer, identity.AuthenticationType);
        Assert.Equal("users/alice", Assert.Single(result.Principal.Claims, claim => claim.Type == IdentityClaims.Subject).Value);
        Assert.Equal("password-client", Assert.Single(result.Principal.Claims, claim => claim.Type == Claims.ClientId).Value);
        Assert.Equal(GrantTypes.Password, result.Properties![Properties.GrantType]);
        Assert.Equal("api", result.Properties[Properties.Scope]);
        Assert.DoesNotContain(result.Properties.Values, value => value?.Contains(Password, StringComparison.Ordinal) == true);
        host.validator.Verify(value => value.ValidateAsync(host.application, "alice", Password, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Public_Client_May_Use_The_Historical_Grant() {
        var application = Application();
        application.ApplicationType = ApplicationTypes.Native;
        var host = CreateHandler(Principal(), application, authenticated: false);

        var result = await host.handler.HandleAsync(Request(), null, null, new AdviceContext(host.services), CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
    }

    [Fact]
    public async Task Confidential_Client_Requires_A_Verified_Credential() {
        var host = CreateHandler(Principal(), authenticated: false);

        var error = await Assert.ThrowsAsync<OAuthException>(() => host.handler.HandleAsync(
            Request(), null, null, new AdviceContext(host.services), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClient, error.Status);
        host.validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Invalid_Client_Credentials_Are_Rejected_Before_User_Validation() {
        var host = CreateHandler(Principal(), authFails: true);

        var error = await Assert.ThrowsAsync<OAuthException>(() => host.handler.HandleAsync(
            Request(), null, null, new AdviceContext(host.services), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClient, error.Status);
        host.validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_Validator_Fails_Closed() {
        var host = CreateHandler(Principal(), registerValidator: false);

        var error = await Assert.ThrowsAsync<OAuthException>(() => host.handler.HandleAsync(
            Request(), null, null, new AdviceContext(host.services), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, error.Status);
    }

    [Fact]
    public async Task Validator_Rejection_Fails_Closed() {
        var host = CreateHandler(null);

        var error = await Assert.ThrowsAsync<OAuthException>(() => host.handler.HandleAsync(
            Request(), null, null, new AdviceContext(host.services), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, error.Status);
    }

    [Theory]
    [InlineData(null, Password, "username")]
    [InlineData("alice", null, "password")]
    [InlineData("alice\r\nadmin", Password, "username")]
    [InlineData("alice", "bad\rpassword", "password")]
    public async Task Malformed_Legacy_Credential_Grammar_Is_An_Invalid_Request(
        string? username,
        string? password,
        string parameter
    ) {
        var host = CreateHandler(Principal());
        var request = Request();
        request.Username = username;
        request.Password = password;

        var error = await Assert.ThrowsAsync<OAuthException>(() => host.handler.HandleAsync(
            request, null, null, new AdviceContext(host.services), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, error.Status);
        var detail = Assert.IsType<ErrorInfoDetail>(Assert.Single(error.Details!));
        var metadata = Assert.IsType<Dictionary<string, string>>(detail.Metadata);
        Assert.Equal(parameter, metadata["value"]);
        host.client.VerifyNoOtherCalls();
        host.validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Empty_Grammar_Values_Are_Left_To_The_Validator() {
        var host = CreateHandler(Principal(), username: string.Empty, password: string.Empty);
        var request = Request();
        request.Username = string.Empty;
        request.Password = string.Empty;

        var result = await host.handler.HandleAsync(request, null, null, new AdviceContext(host.services), CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        host.validator.Verify(value => value.ValidateAsync(host.application, string.Empty, string.Empty, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Client_Without_Password_Grant_Metadata_Is_Rejected() {
        var application = Application();
        application.GrantTypes = [GrantTypes.ClientCredentials];
        var host = CreateHandler(Principal(), application);

        var error = await Assert.ThrowsAsync<OAuthException>(() => host.handler.HandleAsync(
            Request(), null, null, new AdviceContext(host.services), CancellationToken.None));

        Assert.Equal(OAuthErrors.UnauthorizedClient, error.Status);
        host.validator.VerifyNoOtherCalls();
    }

    private static TokenRequest Request() {
        return new() {
            GrantType = GrantTypes.Password,
            ClientId = "password-client",
            ClientSecret = "client-secret",
            Username = "alice",
            Password = Password,
            Scope = "api",
        };
    }

    private static SchemataApplication Application() {
        return new() {
            Uid = Guid.NewGuid(),
            Name = "password-client",
            CanonicalName = "applications/password-client",
            ClientId = "password-client",
            GrantTypes = [GrantTypes.Password],
            Permissions = [],
        };
    }

    private static ClaimsPrincipal Principal() {
        return new(new ClaimsIdentity([
            new Claim(IdentityClaims.Subject, "users/alice"),
            new Claim(IdentityClaims.PreferredUsername, "alice"),
        ], "Identity.Application"));
    }

    private static (
        ResourceOwnerPasswordHandler<SchemataApplication> handler,
        SchemataApplication application,
        Mock<IClientAuthenticationService<SchemataApplication>> client,
        Mock<IResourceOwnerPasswordValidator> validator,
        ServiceProvider services
    ) CreateHandler(
        ClaimsPrincipal? principal,
        SchemataApplication? application = null,
        bool authFails = false,
        bool authenticated = true,
        bool registerValidator = true,
        string username = "alice",
        string password = Password
    ) {
        application ??= Application();
        var client = new Mock<IClientAuthenticationService<SchemataApplication>>(MockBehavior.Strict);
        if (authFails) {
            client.Setup(value => value.AuthenticateAsync(
                    It.IsAny<Dictionary<string, List<string?>>?>(),
                    It.IsAny<Dictionary<string, List<string?>>?>(),
                    It.IsAny<Dictionary<string, List<string?>>?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>()))
                .ThrowsAsync(OAuthException.FromDescription(OAuthErrors.InvalidClient, "invalid client"));
        } else {
            client.Setup(value => value.AuthenticateAsync(
                    It.IsAny<Dictionary<string, List<string?>>?>(),
                    It.IsAny<Dictionary<string, List<string?>>?>(),
                    It.IsAny<Dictionary<string, List<string?>>?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(new ClientAuthenticationResult<SchemataApplication> {
                    Application = application,
                    Method = authenticated ? ClientAuthMethods.ClientSecretPost : ClientAuthMethods.None,
                    Authenticated = authenticated,
                });
        }

        var manager = new Mock<IApplicationManager<SchemataApplication>>(MockBehavior.Strict);
        manager.SetupTypedMetadata();
        var validator = new Mock<IResourceOwnerPasswordValidator>(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        if (registerValidator) {
            validator.Setup(value => value.ValidateAsync(application, username, password, CancellationToken.None))
                     .ReturnsAsync(principal);
            services.AddSingleton(validator.Object);
        }
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            ITokenRequestAdvisor<SchemataApplication>, AdviceRequestGrantPermission<SchemataApplication>>());
        var provider = services.BuildServiceProvider();
        return (new(client.Object, provider), application, client, validator, provider);
    }
}

[Trait("Layer", "Unit")]
public class IdentityResourceOwnerPasswordValidatorShould
{
    private const string Password = "correct-password";

    [Fact]
    public async Task Admitted_User_Uses_Canonical_Principal_Factory_Without_Cookie_Sign_In() {
        var host = CreateValidator(SignInResult.Success);

        var principal = await host.validator.ValidateAsync(new(), "alice", Password, CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal("users/alice", Assert.Single(principal.Claims, claim => claim.Type == IdentityClaims.Subject).Value);
        host.sign.Verify(value => value.CheckPasswordSignInAsync(host.user, Password, true), Times.Once);
        host.sign.Verify(value => value.IsTwoFactorEnabledAsync(host.user), Times.Once);
        host.claims.Verify(value => value.CreateAsync(host.user), Times.Once);
        host.authentication.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unknown_User_Is_Rejected() {
        var host = CreateValidator(SignInResult.Failed, unknownUser: true);

        var principal = await host.validator.ValidateAsync(new(), "alice", Password, CancellationToken.None);

        Assert.Null(principal);
        host.sign.Verify(value => value.CheckPasswordSignInAsync(It.IsAny<SchemataUser>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        host.claims.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Locked_Out_User_Is_Rejected() {
        await AssertRejected(SignInResult.LockedOut);
    }

    [Fact]
    public async Task Two_Factor_User_With_Correct_Password_Is_Rejected() {
        var host = CreateValidator(SignInResult.Success, twoFactor: true);

        var principal = await host.validator.ValidateAsync(new(), "alice", Password, CancellationToken.None);

        Assert.Null(principal);
        host.sign.Verify(value => value.CheckPasswordSignInAsync(host.user, Password, true), Times.Once);
        host.sign.Verify(value => value.IsTwoFactorEnabledAsync(host.user), Times.Once);
        host.claims.VerifyNoOtherCalls();
        host.authentication.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Not_Allowed_User_Is_Rejected() {
        await AssertRejected(SignInResult.NotAllowed);
    }

    [Fact]
    public async Task Administratively_Inactive_User_Is_Rejected_By_Pre_Sign_In_Check() {
        var store = new Mock<IUserStore<SchemataUser>>();
        var options = Options.Create(new IdentityOptions());
        var users = new Mock<SchemataUserManager<SchemataUser>>(MockBehavior.Loose,
            Mock.Of<IServiceProvider>(), store.Object, options, new PasswordHasher<SchemataUser>(),
            Array.Empty<IUserValidator<SchemataUser>>(), Array.Empty<IPasswordValidator<SchemataUser>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        var user = new SchemataUser { UserName = "alice", AdministrativelyActive = false };
        users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(user);
        users.Setup(value => value.IsAdministrativelyActiveAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        users.SetupGet(value => value.SupportsUserLockout).Returns(true);
        var claims = new Mock<IUserClaimsPrincipalFactory<SchemataUser>>(MockBehavior.Strict);
        var authentication = new Mock<IAuthenticationService>(MockBehavior.Strict);
        using var services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var manager = users.Object;
        var sign = new SchemataSignInManager<SchemataUser>(manager,
            new HttpContextAccessor { HttpContext = context }, claims.Object, options,
            NullLogger<SignInManager<SchemataUser>>.Instance, Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<SchemataUser>>(),
            new ConfiguredUserAliasResolver(UserNameAliasMode.Disabled, new E164PhoneNumberNormalizer()), []);
        var validator = new IdentityResourceOwnerPasswordValidator<SchemataUser>(manager, sign, claims.Object);

        var principal = await validator.ValidateAsync(new(), "alice", Password, CancellationToken.None);

        Assert.Null(principal);
        users.Verify(value => value.CheckPasswordAsync(It.IsAny<SchemataUser>(), It.IsAny<string>()), Times.Never);
        claims.VerifyNoOtherCalls();
        authentication.VerifyNoOtherCalls();
    }

    private static async Task AssertRejected(SignInResult result) {
        var host = CreateValidator(result);

        var principal = await host.validator.ValidateAsync(new(), "alice", Password, CancellationToken.None);

        Assert.Null(principal);
        host.sign.Verify(value => value.CheckPasswordSignInAsync(host.user, Password, true), Times.Once);
        host.claims.VerifyNoOtherCalls();
        host.authentication.VerifyNoOtherCalls();
    }

    private static (
        IdentityResourceOwnerPasswordValidator<SchemataUser> validator,
        SchemataUser user,
        Mock<SignInManager<SchemataUser>> sign,
        Mock<IUserClaimsPrincipalFactory<SchemataUser>> claims,
        Mock<IAuthenticationService> authentication
    ) CreateValidator(SignInResult result, SchemataUser? user = null, bool unknownUser = false, bool twoFactor = false)
    {
        user ??= new() { UserName = "alice", CanonicalName = "users/alice" };
        var users = new Mock<SchemataUserManager<SchemataUser>>(MockBehavior.Loose,
            Mock.Of<IServiceProvider>(), Mock.Of<IUserStore<SchemataUser>>(), Options.Create(new IdentityOptions()),
            new PasswordHasher<SchemataUser>(), Array.Empty<IUserValidator<SchemataUser>>(),
            Array.Empty<IPasswordValidator<SchemataUser>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(unknownUser ? null : user);
        var claims = new Mock<IUserClaimsPrincipalFactory<SchemataUser>>(MockBehavior.Strict);
        claims.Setup(value => value.CreateAsync(user)).ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(IdentityClaims.Subject, "users/alice"),
        ], "Identity.Application")));
        var authentication = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var context = new DefaultHttpContext {
            RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider(),
        };
        var sign = new Mock<SignInManager<SchemataUser>>(MockBehavior.Loose,
            users.Object, new HttpContextAccessor { HttpContext = context }, claims.Object,
            Options.Create(new IdentityOptions()), NullLogger<SignInManager<SchemataUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(), Mock.Of<IUserConfirmation<SchemataUser>>());
        sign.Setup(value => value.CheckPasswordSignInAsync(user, Password, true)).ReturnsAsync(result);
        sign.Setup(value => value.IsTwoFactorEnabledAsync(user)).ReturnsAsync(twoFactor);
        return (new(users.Object, sign.Object, claims.Object), user, sign, claims, authentication);
    }
}
