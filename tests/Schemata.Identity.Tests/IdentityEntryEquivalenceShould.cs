using Schemata.Identity.Tests.Fixtures;
using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Stores;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Xunit;
using AspNetIdentityResult = Microsoft.AspNetCore.Identity.IdentityResult;

namespace Schemata.Identity.Tests;

public class IdentityEntryEquivalenceShould
{
    [Fact]
    public void Feature_Registers_All_Fourteen_Closed_Handlers() {
        var services = new ServiceCollection();
        services.AddSchemataIdentity<
            SchemataUser,
            SchemataRole,
            SchemataUserStore<SchemataUser>,
            SchemataRoleStore<SchemataRole>>(_ => { }, _ => { });

        AssertHandler<RegisterUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>, RegisterUserHandler<SchemataUser>>(services);
        AssertHandler<LoginUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>, LoginUserHandler<SchemataUser>>(services);
        AssertHandler<RefreshUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>, RefreshUserHandler<SchemataUser>>(services);
        AssertHandler<GetUserProfileQuery<SchemataUser>, IdentityResult<ClaimsStore>, GetUserProfileHandler<SchemataUser>>(services);
        AssertHandler<ChangeUserEmailRequest<SchemataUser>, IdentityResult<Unit>, ChangeUserEmailHandler<SchemataUser>>(services);
        AssertHandler<ChangeUserPhoneRequest<SchemataUser>, IdentityResult<Unit>, ChangeUserPhoneHandler<SchemataUser>>(services);
        AssertHandler<ChangeUserPasswordRequest<SchemataUser>, IdentityResult<Unit>, ChangeUserPasswordHandler<SchemataUser>>(services);
        AssertHandler<ForgotUserPasswordRequest<SchemataUser>, IdentityResult<Unit>, ForgotUserPasswordHandler<SchemataUser>>(services);
        AssertHandler<ResetUserPasswordRequest<SchemataUser>, IdentityResult<Unit>, ResetUserPasswordHandler<SchemataUser>>(services);
        AssertHandler<ConfirmUserRequest<SchemataUser>, IdentityResult<Unit>, ConfirmUserHandler<SchemataUser>>(services);
        AssertHandler<SendUserConfirmationCodeRequest<SchemataUser>, IdentityResult<Unit>, SendUserConfirmationCodeHandler<SchemataUser>>(services);
        AssertHandler<GetUserAuthenticatorRequest<SchemataUser>, IdentityResult<AuthenticatorResponse>, GetUserAuthenticatorHandler<SchemataUser>>(services);
        AssertHandler<EnrollUserAuthenticatorRequest<SchemataUser>, IdentityResult<Unit>, EnrollUserAuthenticatorHandler<SchemataUser>>(services);
        AssertHandler<DowngradeUserAuthenticatorRequest<SchemataUser>, IdentityResult<Unit>, DowngradeUserAuthenticatorHandler<SchemataUser>>(services);
    }

    [Fact]
    public void All_Fourteen_Contracts_Round_Trip_Without_Process_Local_Context() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var ticket    = new AuthenticationTicket(principal, "refresh");

        var register = RoundTrip(new RegisterUserRequest<SchemataUser>(new() {
            Username     = "alice",
            EmailAddress = "alice@example.com",
            PhoneNumber  = "+15550001",
            Password     = "secret-1",
            UseCookies   = true,
        }, principal)).Request;
        Assert.Equal("alice", register.Username);
        Assert.Equal("alice@example.com", register.EmailAddress);
        Assert.Equal("+15550001", register.PhoneNumber);
        Assert.Equal("secret-1", register.Password);
        Assert.True(register.UseCookies);

        var login = RoundTrip(new LoginUserRequest<SchemataUser>(new() {
            Username              = "alice",
            Password              = "secret-2",
            TwoFactorCode         = "012345",
            TwoFactorRecoveryCode = "recovery-1",
            AcrValues             = "mfa",
            UseCookies            = false,
        }, principal)).Request;
        Assert.Equal("alice", login.Username);
        Assert.Equal("secret-2", login.Password);
        Assert.Equal("012345", login.TwoFactorCode);
        Assert.Equal("recovery-1", login.TwoFactorRecoveryCode);
        Assert.Equal("mfa", login.AcrValues);
        Assert.False(login.UseCookies);

        var refresh = RoundTrip(new RefreshUserRequest<SchemataUser>(ticket, principal));
        Assert.Null(refresh.Ticket);
        AssertEmptyWire(new RefreshUserRequest<SchemataUser>(ticket, principal));

        Assert.NotNull(RoundTrip(new GetUserProfileQuery<SchemataUser>(principal)));
        AssertEmptyWire(new GetUserProfileQuery<SchemataUser>(principal));

        AssertProfile(RoundTrip(new ChangeUserEmailRequest<SchemataUser>(new() {
            EmailAddress = "email-1@example.com",
            PhoneNumber  = "+15550002",
            OldPassword  = "old-1",
            NewPassword  = "new-1",
        }, principal)).Request, "email-1@example.com", "+15550002", "old-1", "new-1");

        AssertProfile(RoundTrip(new ChangeUserPhoneRequest<SchemataUser>(new() {
            EmailAddress = "phone-1@example.com",
            PhoneNumber  = "+15550003",
            OldPassword  = "old-2",
            NewPassword  = "new-2",
        }, principal)).Request, "phone-1@example.com", "+15550003", "old-2", "new-2");

        AssertProfile(RoundTrip(new ChangeUserPasswordRequest<SchemataUser>(new() {
            EmailAddress = "password-1@example.com",
            PhoneNumber  = "+15550004",
            OldPassword  = "old-3",
            NewPassword  = "new-3",
        }, principal)).Request, "password-1@example.com", "+15550004", "old-3", "new-3");

        AssertContact(RoundTrip(new ForgotUserPasswordRequest<SchemataUser>(new() {
            EmailAddress = "forgot-1@example.com",
            PhoneNumber  = "+15550005",
        }, principal)).Request, "forgot-1@example.com", "+15550005");

        var reset = RoundTrip(new ResetUserPasswordRequest<SchemataUser>(new() {
            EmailAddress = "reset-1@example.com",
            PhoneNumber  = "+15550006",
            Code         = "reset-code-1",
            Password     = "new-secret-1",
        }, principal)).Request;
        Assert.Equal("reset-1@example.com", reset.EmailAddress);
        Assert.Equal("+15550006", reset.PhoneNumber);
        Assert.Equal("reset-code-1", reset.Code);
        Assert.Equal("new-secret-1", reset.Password);

        var confirm = RoundTrip(new ConfirmUserRequest<SchemataUser>(new() {
            EmailAddress = "confirm-1@example.com",
            PhoneNumber  = "+15550007",
            Code         = "confirm-code-1",
        }, principal)).Request;
        Assert.Equal("confirm-1@example.com", confirm.EmailAddress);
        Assert.Equal("+15550007", confirm.PhoneNumber);
        Assert.Equal("confirm-code-1", confirm.Code);

        AssertContact(RoundTrip(new SendUserConfirmationCodeRequest<SchemataUser>(new() {
            EmailAddress = "send-code-1@example.com",
            PhoneNumber  = "+15550008",
        }, principal)).Request, "send-code-1@example.com", "+15550008");

        Assert.NotNull(RoundTrip(new GetUserAuthenticatorRequest<SchemataUser>(principal)));
        AssertEmptyWire(new GetUserAuthenticatorRequest<SchemataUser>(principal));

        AssertAuthenticator(RoundTrip(new EnrollUserAuthenticatorRequest<SchemataUser>(new() {
            TwoFactorCode         = "654321",
            TwoFactorRecoveryCode = "recovery-2",
        }, principal)).Request, "654321", "recovery-2");

        AssertAuthenticator(RoundTrip(new DowngradeUserAuthenticatorRequest<SchemataUser>(new() {
            TwoFactorCode         = "098765",
            TwoFactorRecoveryCode = "recovery-3",
        }, principal)).Request, "098765", "recovery-3");

        var alice = RoundTrip(new RegisterUserRequest<SchemataUser>(
                                  new() { Username = "alice", Password = "shared" }, principal)).Request;
        var bob = RoundTrip(new RegisterUserRequest<SchemataUser>(
                                new() { Username = "bob", Password = "shared" }, principal)).Request;
        Assert.NotEqual(alice.Username, bob.Username);
        Assert.Equal(alice.Password, bob.Password);
    }

    [Fact]
    public async Task Facade_And_Bare_Dispatcher_Share_Command_Query_And_Identity_Advice_Context() {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "alice")], "test"));
        var returnedPrincipal = new ClaimsPrincipal(new ClaimsIdentity("returned"));
        var commandAdvisor = CommandAdvisor();
        var queryAdvisor   = QueryAdvisor();
        var registerAdvisor = IdentityAdvisor<RegisterRequest>(IdentityOperation.Register, principal);
        var profileAdvisor  = IdentityAdvisor<Unit>(IdentityOperation.Profile, principal);
        using var host = new IdentityHandlerTestHost(services => {
            services.AddSingleton(commandAdvisor.Object);
            services.AddSingleton(queryAdvisor.Object);
            services.AddSingleton(registerAdvisor.Object);
            services.AddSingleton(profileAdvisor.Object);
        });
        host.Users.Setup(value => value.CreateAsync(It.IsAny<SchemataUser>(), It.IsAny<string>()))
                  .ReturnsAsync(AspNetIdentityResult.Success);
        host.SignIn.Setup(value => value.CreateUserPrincipalAsync(It.IsAny<SchemataUser>()))
                   .ReturnsAsync(returnedPrincipal);
        host.Users.Setup(value => value.GetUserAsync(principal)).ReturnsAsync(new SchemataUser());
        var register = new RegisterRequest { Username = "alice", Password = "secret" };

        var facadeCommand = await host.Handler.RegisterAsync(register, principal);
        var directCommand = await host.Dispatcher.SendAsync<
            RegisterUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>>(new(register, principal));
        var facadeQuery = await host.Handler.ProfileAsync(principal);
        var directQuery = await host.Dispatcher.SendAsync<
            GetUserProfileQuery<SchemataUser>, IdentityResult<ClaimsStore>>(new(principal));

        Assert.Same(returnedPrincipal, facadeCommand.Data);
        Assert.Same(facadeCommand.Data, directCommand.Data);
        Assert.Equal(facadeQuery.Status, directQuery.Status);
        commandAdvisor.Verify(value => value.AdviseAsync(
                                  It.IsAny<AdviceContext>(),
                                  It.IsAny<RegisterUserRequest<SchemataUser>>(),
                                  It.IsAny<RequestHandlerContinuation<IdentityResult<ClaimsPrincipal>>>(),
                                  It.IsAny<CancellationToken>()), Times.Exactly(2));
        queryAdvisor.Verify(value => value.AdviseAsync(
                                It.IsAny<AdviceContext>(),
                                It.IsAny<GetUserProfileQuery<SchemataUser>>(),
                                It.IsAny<RequestHandlerContinuation<IdentityResult<ClaimsStore>>>(),
                                It.IsAny<CancellationToken>()), Times.Exactly(2));
        registerAdvisor.Verify(value => value.AdviseAsync(
                                   It.IsAny<AdviceContext>(),
                                   It.IsAny<RegisterRequest>(),
                                   IdentityOperation.Register,
                                   principal,
                                   It.IsAny<CancellationToken>()), Times.Exactly(2));
        profileAdvisor.Verify(value => value.AdviseAsync(
                                  It.IsAny<AdviceContext>(),
                                  Unit.Value,
                                  IdentityOperation.Profile,
                                  principal,
                                  It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Operation_Handler_Rejects_Direct_Calls_Without_Dispatcher_Context() {
        using var host = new IdentityHandlerTestHost();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Operations.RegisterAsync(new(), new()));

        Assert.Contains("ambient AdviceContext is required", exception.Message, StringComparison.Ordinal);
    }

    private static Mock<IRequestPipelineAdvisor<RegisterUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>>> CommandAdvisor() {
        var advisor = new Mock<IRequestPipelineAdvisor<RegisterUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<RegisterUserRequest<SchemataUser>>(),
                          It.IsAny<RequestHandlerContinuation<IdentityResult<ClaimsPrincipal>>>(),
                          It.IsAny<CancellationToken>()))
               .Returns((AdviceContext ctx, RegisterUserRequest<SchemataUser> _, RequestHandlerContinuation<IdentityResult<ClaimsPrincipal>> next, CancellationToken ct) => {
                   ctx.Set(new Marker());
                   return next(ct);
               });
        return advisor;
    }

    private static Mock<IRequestPipelineAdvisor<GetUserProfileQuery<SchemataUser>, IdentityResult<ClaimsStore>>> QueryAdvisor() {
        var advisor = new Mock<IRequestPipelineAdvisor<GetUserProfileQuery<SchemataUser>, IdentityResult<ClaimsStore>>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<GetUserProfileQuery<SchemataUser>>(),
                          It.IsAny<RequestHandlerContinuation<IdentityResult<ClaimsStore>>>(),
                          It.IsAny<CancellationToken>()))
               .Returns((AdviceContext ctx, GetUserProfileQuery<SchemataUser> _, RequestHandlerContinuation<IdentityResult<ClaimsStore>> next, CancellationToken ct) => {
                   ctx.Set(new Marker());
                   return next(ct);
               });
        return advisor;
    }

    private static Mock<IIdentityRequestAdvisor<TRequest>> IdentityAdvisor<TRequest>(
        IdentityOperation operation,
        ClaimsPrincipal   principal
    ) {
        var advisor = new Mock<IIdentityRequestAdvisor<TRequest>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<TRequest>(),
                          operation,
                          principal,
                          It.IsAny<CancellationToken>()))
               .Returns((AdviceContext ctx, TRequest _, IdentityOperation _, ClaimsPrincipal _, CancellationToken _) => {
                   Assert.True(ctx.TryGet<Marker>(out _));
                   return Task.FromResult(AdviseResult.Continue);
               });
        return advisor;
    }

    private static T RoundTrip<T>(T request) where T : class, IRequestPrincipal {
        var json = JsonSerializer.Serialize(request, SchemataJson.Default);
        Assert.DoesNotContain("principal", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ticket", json, StringComparison.OrdinalIgnoreCase);
        var result = Assert.IsType<T>(JsonSerializer.Deserialize<T>(json, SchemataJson.Default));
        Assert.Null(result.Principal);
        return result;
    }

    private static void AssertEmptyWire<T>(T request) where T : class, IRequestPrincipal {
        Assert.Equal("{}", JsonSerializer.Serialize(request, SchemataJson.Default));
    }

    private static void AssertProfile(ProfileRequest request, string email, string phone, string oldPassword, string newPassword) {
        Assert.Equal(email, request.EmailAddress);
        Assert.Equal(phone, request.PhoneNumber);
        Assert.Equal(oldPassword, request.OldPassword);
        Assert.Equal(newPassword, request.NewPassword);
    }

    private static void AssertContact(ForgetRequest request, string email, string phone) {
        Assert.Equal(email, request.EmailAddress);
        Assert.Equal(phone, request.PhoneNumber);
    }

    private static void AssertAuthenticator(AuthenticatorRequest request, string code, string recovery) {
        Assert.Equal(code, request.TwoFactorCode);
        Assert.Equal(recovery, request.TwoFactorRecoveryCode);
    }

    private static void AssertHandler<TRequest, TResponse, THandler>(IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : IRequestHandler<TRequest, TResponse> {
        var service = typeof(IRequestHandler<TRequest, TResponse>);
        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == service);
        Assert.Equal(typeof(THandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    private sealed record Marker;
}
