using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Tests.Fixtures;
using Xunit;
using AspNetIdentityResult = Microsoft.AspNetCore.Identity.IdentityResult;

namespace Schemata.Identity.Tests;

public class IdentityOperationHandlerShould
{
    private const string Email      = "alice@example.com";
    private const string Code       = "confirmation-code";
    private const string OldPassword = "old-secret";
    private const string NewPassword = "new-secret";

    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity("test"));

    private static readonly ClaimsPrincipal Anonymous = new();

    private static readonly SchemataUser User = new() { UserName = "alice", Email = Email };

    [Fact]
    public async Task Profile_Change_Advisor_Handles_Through_The_Operation_Carrier() {
        var expected = IdentityResult<Unit>.Success(null);
        IdentityOperationExecution<IdentityResult<Unit>>? execution = null;
        IHandledResponse<IdentityResult<Unit>>? carrier = null;
        var request = RequestAdvisor<ProfileRequest, Unit>(value => execution = value);
        var change = new Mock<IIdentityProfileChangeAdvisor>();
        change.SetupGet(value => value.Order).Returns(0);
        change.Setup(value => value.AdviseAsync(
                         It.IsAny<AdviceContext>(),
                         It.IsAny<SchemataUser>(),
                         It.IsAny<IdentityOperation>(),
                         It.IsAny<IHandledResponse<IdentityResult<Unit>>>(),
                         It.IsAny<CancellationToken>()))
              .Returns((AdviceContext _,
                        SchemataUser _,
                        IdentityOperation _,
                        IHandledResponse<IdentityResult<Unit>> output,
                        CancellationToken _) => {
                  carrier = output;
                  output.SetResponse(expected);
                  return Task.FromResult(AdviseResult.Handle);
              });
        using var host = new IdentityHandlerTestHost(services => {
            services.AddSingleton(request.Object);
            services.AddSingleton(change.Object);
        });
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.ChangePasswordAsync(User, OldPassword, NewPassword))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.ChangePasswordAsync(new() {
            OldPassword = OldPassword,
            NewPassword = NewPassword,
        }, Principal);

        Assert.NotNull(carrier);
        Assert.Same(execution, carrier);
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Registration_Stages_Share_One_Operation_Carrier() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        IdentityOperationExecution<IdentityResult<ClaimsPrincipal>>? execution = null;
        IHandledResponse<IdentityResult<ClaimsPrincipal>>? userCarrier       = null;
        IHandledResponse<IdentityResult<ClaimsPrincipal>>? registrationCarrier = null;
        var request = RequestAdvisor<RegisterRequest, ClaimsPrincipal>(value => execution = value);
        var beforeCreate = new Mock<IIdentityRegisterUserAdvisor>();
        beforeCreate.SetupGet(value => value.Order).Returns(0);
        beforeCreate.Setup(value => value.AdviseAsync(
                                It.IsAny<AdviceContext>(),
                                It.IsAny<SchemataUser>(),
                                It.IsAny<RegisterRequest>(),
                                It.IsAny<IHandledResponse<IdentityResult<ClaimsPrincipal>>>(),
                                It.IsAny<CancellationToken>()))
                    .Callback((AdviceContext _,
                               SchemataUser _,
                               RegisterRequest _,
                               IHandledResponse<IdentityResult<ClaimsPrincipal>> output,
                               CancellationToken _) => userCarrier = output)
                    .ReturnsAsync(AdviseResult.Continue);
        var afterCreate = new Mock<IIdentityRegisterAdvisor<SchemataUser>>();
        afterCreate.SetupGet(value => value.Order).Returns(0);
        afterCreate.Setup(value => value.AdviseAsync(
                               It.IsAny<AdviceContext>(),
                               It.IsAny<SchemataUser>(),
                               It.IsAny<RegisterRequest>(),
                               It.IsAny<IHandledResponse<IdentityResult<ClaimsPrincipal>>>(),
                               It.IsAny<CancellationToken>()))
                   .Callback((AdviceContext _,
                              SchemataUser _,
                              RegisterRequest _,
                              IHandledResponse<IdentityResult<ClaimsPrincipal>> output,
                              CancellationToken _) => registrationCarrier = output)
                   .ReturnsAsync(AdviseResult.Continue);
        using var host = new IdentityHandlerTestHost(services => {
            services.AddSingleton(request.Object);
            services.AddSingleton(beforeCreate.Object);
            services.AddSingleton(afterCreate.Object);
        });
        host.Users.Setup(value => value.CreateAsync(It.IsAny<SchemataUser>(), NewPassword))
                  .ReturnsAsync(AspNetIdentityResult.Success);
        host.SignIn.Setup(value => value.CreateUserPrincipalAsync(It.IsAny<SchemataUser>()))
                   .ReturnsAsync(principal);

        var result = await host.Handler.RegisterAsync(new() {
            Username = "alice",
            Password = NewPassword,
        }, Principal);

        Assert.NotNull(userCarrier);
        Assert.NotNull(registrationCarrier);
        Assert.Same(execution, userCarrier);
        Assert.Same(execution, registrationCarrier);
        Assert.Same(principal, result.Data);
    }

    [Fact]
    public async Task Recovery_Advisor_Block_Rejects_Reset() {
        IdentityOperationExecution<IdentityResult<Unit>>? execution = null;
        IHandledResponse<IdentityResult<Unit>>? carrier = null;
        var request = RequestAdvisor<ResetRequest, Unit>(value => execution = value);
        var recovery = new Mock<IIdentityRecoveryAdvisor>();
        recovery.SetupGet(value => value.Order).Returns(0);
        recovery.Setup(value => value.AdviseAsync(
                            It.IsAny<AdviceContext>(),
                            It.IsAny<SchemataUser>(),
                            It.IsAny<IdentityOperation>(),
                            It.IsAny<IHandledResponse<IdentityResult<Unit>>>(),
                            It.IsAny<CancellationToken>()))
                .Callback((AdviceContext _,
                           SchemataUser _,
                           IdentityOperation _,
                           IHandledResponse<IdentityResult<Unit>> output,
                           CancellationToken _) => carrier = output)
                .ReturnsAsync(AdviseResult.Block);
        using var host = new IdentityHandlerTestHost(services => {
            services.AddSingleton(request.Object);
            services.AddSingleton(recovery.Object);
        });
        host.Users.Setup(value => value.FindByEmailAsync(Email)).ReturnsAsync(User);
        host.Users.Setup(value => value.IsEmailConfirmedAsync(User)).ReturnsAsync(true);
        host.Users.Setup(value => value.ResetPasswordAsync(User, Code, NewPassword))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => host.Handler.ResetAsync(new() {
            EmailAddress = Email,
            Code         = Code,
            Password     = NewPassword,
        }, Anonymous));

        Assert.NotNull(carrier);
        Assert.Same(execution, carrier);
    }

    [Fact]
    public async Task Confirmation_Advisor_Handles_Through_The_Operation_Carrier() {
        var expected = IdentityResult<Unit>.Success(null);
        IdentityOperationExecution<IdentityResult<Unit>>? execution = null;
        IHandledResponse<IdentityResult<Unit>>? carrier = null;
        var request = RequestAdvisor<ConfirmRequest, Unit>(value => execution = value);
        var recovery = new Mock<IIdentityRecoveryAdvisor>();
        recovery.SetupGet(value => value.Order).Returns(0);
        recovery.Setup(value => value.AdviseAsync(
                            It.IsAny<AdviceContext>(),
                            It.IsAny<SchemataUser>(),
                            It.IsAny<IdentityOperation>(),
                            It.IsAny<IHandledResponse<IdentityResult<Unit>>>(),
                            It.IsAny<CancellationToken>()))
                .Returns((AdviceContext _,
                          SchemataUser _,
                          IdentityOperation _,
                          IHandledResponse<IdentityResult<Unit>> output,
                          CancellationToken _) => {
                    carrier = output;
                    output.SetResponse(expected);
                    return Task.FromResult(AdviseResult.Handle);
                });
        using var host = new IdentityHandlerTestHost(services => {
            services.AddSingleton(request.Object);
            services.AddSingleton(recovery.Object);
        });
        host.Users.Setup(value => value.FindByEmailAsync(Email)).ReturnsAsync(User);
        host.Users.Setup(value => value.ChangeEmailAsync(User, Email, Code))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.ConfirmAsync(new() {
            EmailAddress = Email,
            Code         = Code,
        }, Anonymous);

        Assert.NotNull(carrier);
        Assert.Same(execution, carrier);
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Request_Stage_Write_That_Continues_Does_Not_Handle_The_Profile_Change_Stage() {
        var stray = IdentityResult<Unit>.Success(null);
        IdentityOperationExecution<IdentityResult<Unit>>? execution = null;
        var request = new Mock<IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>>>();
        request.SetupGet(value => value.Order).Returns(0);
        request.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<ProfileRequest>(),
                          It.IsAny<IdentityOperationExecution<IdentityResult<Unit>>>(),
                          It.IsAny<CancellationToken>()))
               .Returns((AdviceContext _,
                         ProfileRequest _,
                         IdentityOperationExecution<IdentityResult<Unit>> payload,
                         CancellationToken _) => {
                   execution = payload;
                   payload.SetResponse(stray);
                   return Task.FromResult(AdviseResult.Continue);
               });
        var change = new Mock<IIdentityProfileChangeAdvisor>();
        change.SetupGet(value => value.Order).Returns(0);
        change.Setup(value => value.AdviseAsync(
                         It.IsAny<AdviceContext>(),
                         It.IsAny<SchemataUser>(),
                         It.IsAny<IdentityOperation>(),
                         It.IsAny<IHandledResponse<IdentityResult<Unit>>>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(AdviseResult.Handle);
        using var host = new IdentityHandlerTestHost(services => {
            services.AddSingleton(request.Object);
            services.AddSingleton(change.Object);
        });
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.ChangePasswordAsync(User, OldPassword, NewPassword))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => host.Handler.ChangePasswordAsync(new() {
            OldPassword = OldPassword,
            NewPassword = NewPassword,
        }, Principal));

        Assert.NotNull(execution);
        Assert.Null(execution.Response);
    }

    private static Mock<IIdentityRequestAdvisor<TRequest, IdentityResult<TResponse>>> RequestAdvisor<TRequest, TResponse>(
        System.Action<IdentityOperationExecution<IdentityResult<TResponse>>> capture
    ) where TRequest : class
      where TResponse : class {
        var advisor = new Mock<IIdentityRequestAdvisor<TRequest, IdentityResult<TResponse>>>();
        advisor.SetupGet(value => value.Order).Returns(0);
        advisor.Setup(value => value.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          It.IsAny<TRequest>(),
                          It.IsAny<IdentityOperationExecution<IdentityResult<TResponse>>>(),
                          It.IsAny<CancellationToken>()))
               .Callback((AdviceContext _,
                          TRequest _,
                          IdentityOperationExecution<IdentityResult<TResponse>> execution,
                          CancellationToken _) => capture(execution))
               .ReturnsAsync(AdviseResult.Continue);
        return advisor;
    }
}
