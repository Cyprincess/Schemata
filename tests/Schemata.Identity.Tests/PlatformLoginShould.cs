using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Services;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Skeleton.Stores;
using Xunit;
using PlatformResult = Microsoft.AspNetCore.Identity.IdentityResult;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Integration")]
public class PlatformLoginShould
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_Lockout_Persistence_Prevents_Final_Issuance(bool reset) {
        var store = new Mock<IUserStore<SchemataUser>>();
        var options = Options.Create(new IdentityOptions());
        using var users = new Mock<SchemataUserManager<SchemataUser>>(MockBehavior.Loose, Mock.Of<IServiceProvider>(),
            store.Object, options, new PasswordHasher<SchemataUser>(),
            Array.Empty<IUserValidator<SchemataUser>>(), Array.Empty<IPasswordValidator<SchemataUser>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            NullLogger<SchemataUserManager<SchemataUser>>.Instance).Object;
        var mockedUsers = Mock.Get(users);
        var user = new SchemataUser { UserName = "alice" };
        mockedUsers.Setup(u => u.FindByNameAsync("alice")).ReturnsAsync(user);
        mockedUsers.Setup(u => u.IsAdministrativelyActiveAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        mockedUsers.Setup(u => u.CheckPasswordAsync(user, "password")).ReturnsAsync(true);
        mockedUsers.SetupGet(u => u.SupportsUserLockout).Returns(true);
        mockedUsers.SetupGet(u => u.SupportsUserTwoFactor).Returns(true);
        mockedUsers.Setup(u => u.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        mockedUsers.Setup(u => u.GetValidTwoFactorProvidersAsync(user)).ReturnsAsync(new List<string> { options.Value.Tokens.AuthenticatorTokenProvider });
        mockedUsers.Setup(u => u.GetUserIdAsync(user)).ReturnsAsync("user-id");
        mockedUsers.Setup(u => u.VerifyTwoFactorTokenAsync(user, options.Value.Tokens.AuthenticatorTokenProvider, "123456")).ReturnsAsync(reset);
        var conflict = PlatformResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        mockedUsers.Setup(u => u.ResetAccessFailedCountAsync(user)).ReturnsAsync(conflict);
        mockedUsers.Setup(u => u.AccessFailedAsync(user)).ReturnsAsync(conflict);
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
        var observer = new Mock<IHostSignInObserver>();
        var advisor = new Mock<IIdentityLoginAdvisor>();
        using var services = new ServiceCollection().AddSingleton(authentication.Object).AddSingleton(advisor.Object).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        var advice = new AdviceContext(services);
        var sign = new SchemataSignInManager<SchemataUser>(users, new HttpContextAccessor { HttpContext = context },
            Mock.Of<IUserClaimsPrincipalFactory<SchemataUser>>(), options, NullLogger<SignInManager<SchemataUser>>.Instance,
            schemes.Object, Mock.Of<IUserConfirmation<SchemataUser>>(),
            new ConfiguredUserAliasResolver(UserNameAliasMode.Disabled, new E164PhoneNumberNormalizer()), [observer.Object]);

        await Assert.ThrowsAsync<UnauthenticatedException>(() => sign.LoginAsync(new LoginRequest {
            Username = "alice", Password = "password", TwoFactorCode = "123456", UseCookies = true,
        }, new ClaimsPrincipal(), advice));

        authentication.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), It.Is<string>(s =>
            s == IdentityConstants.ApplicationScheme || s == IdentityConstants.BearerScheme),
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Never);
        observer.Verify(o => o.OnSigningInAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()), Times.Never);
        advisor.Verify(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<SchemataUser>(), It.IsAny<LoginRequest>(), It.IsAny<IHandledResponse<IdentityResult<ClaimsPrincipal>>>(), It.IsAny<CancellationToken>()), Times.Never);
        if (reset) mockedUsers.Verify(u => u.ResetAccessFailedCountAsync(user), Times.Once);
        else mockedUsers.Verify(u => u.AccessFailedAsync(user), Times.Once);
    }

    [Theory]
    [InlineData("trailing ")]
    [InlineData("two  spaces")]
    [InlineData("a·b")]
    public async Task Malformed_Username_Fails_Login_Without_Lookup_Or_Credential_Work(string userName) {
        var repository = new Mock<IRepository<SchemataUser>>(MockBehavior.Strict);
        var authentication = new Mock<IAuthenticationService>();
        var observer = new Mock<IHostSignInObserver>();
        var advisor = new Mock<IIdentityLoginAdvisor>();
        var registrations = new ServiceCollection();
        registrations.AddSingleton(repository.Object);
        registrations.AddSingleton<ILookupNormalizer, UsernameCaseMappedNormalizer>();
        registrations.AddSingleton(Mock.Of<IRepository<SchemataUserClaim>>());
        registrations.AddSingleton(Mock.Of<IRepository<SchemataUserRole>>());
        registrations.AddSingleton(Mock.Of<IRepository<SchemataUserLogin>>());
        registrations.AddSingleton(Mock.Of<IRepository<SchemataUserToken>>());
        registrations.AddSingleton(authentication.Object);
        registrations.AddSingleton(advisor.Object);
        using var services = registrations.BuildServiceProvider();
        var store = new SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole,
            SchemataUserLogin, SchemataUserToken, SchemataRoleClaim>(
            services.GetRequiredService<IRepository<SchemataUserClaim>>(),
            services.GetRequiredService<IRepository<SchemataUserRole>>(),
            services.GetRequiredService<IRepository<SchemataUserLogin>>(),
            services.GetRequiredService<IRepository<SchemataUserToken>>(),
            Mock.Of<IResourceMutation<SchemataUser>>(), services);
        var options = Options.Create(new IdentityOptions());
        using var users = new SchemataUserManager<SchemataUser>(services, store, options,
            new PasswordHasher<SchemataUser>(), [], [], new UsernameCaseMappedNormalizer(),
            new IdentityErrorDescriber(), NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        var context = new DefaultHttpContext { RequestServices = services };
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        var sign = new SchemataSignInManager<SchemataUser>(users, new HttpContextAccessor { HttpContext = context },
            Mock.Of<IUserClaimsPrincipalFactory<SchemataUser>>(), options, NullLogger<SignInManager<SchemataUser>>.Instance,
            schemes.Object, Mock.Of<IUserConfirmation<SchemataUser>>(),
            new ConfiguredUserAliasResolver(UserNameAliasMode.Disabled, new E164PhoneNumberNormalizer()), [observer.Object]);

        await Assert.ThrowsAsync<UnauthenticatedException>(() => sign.LoginAsync(new LoginRequest {
            Username = userName, Password = "password", UseCookies = true,
        }, new ClaimsPrincipal(), new AdviceContext(services)));

        repository.VerifyNoOtherCalls();
        authentication.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
            It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()), Times.Never);
        observer.Verify(o => o.OnSigningInAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<ClaimsPrincipal>(),
            It.IsAny<CancellationToken>()), Times.Never);
        advisor.Verify(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<SchemataUser>(), It.IsAny<LoginRequest>(),
            It.IsAny<IHandledResponse<IdentityResult<ClaimsPrincipal>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
