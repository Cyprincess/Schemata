using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;

namespace Schemata.Identity.Tests.Fixtures;

internal sealed class IdentityHandlerTestHost : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly ServiceProvider _managerServices;

    internal IdentityHandlerTestHost(Action<IServiceCollection>? configure = null) {
        var options = new IdentityOptions();
        _managerServices = new ServiceCollection().BuildServiceProvider();
        var store = new Mock<IUserStore<SchemataUser>>();
        Users = new Mock<SchemataUserManager<SchemataUser>>(
            MockBehavior.Loose,
            _managerServices,
            store.Object,
            Options.Create(options),
            new PasswordHasher<SchemataUser>(),
            Array.Empty<IUserValidator<SchemataUser>>(),
            Array.Empty<IPasswordValidator<SchemataUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        SignIn = new Mock<SignInManager<SchemataUser>>(
            MockBehavior.Loose,
            Users.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<SchemataUser>>(),
            Options.Create(options),
            NullLogger<SignInManager<SchemataUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<SchemataUser>>());
        Mail    = new Mock<IMailSender<SchemataUser>>(MockBehavior.Strict);
        Message = new Mock<IMessageSender<SchemataUser>>(MockBehavior.Strict);

        var services = new ServiceCollection();
        services.AddSingleton(Users.Object);
        services.AddSingleton(SignIn.Object);
        services.AddSingleton(Mail.Object);
        services.AddSingleton(Message.Object);
        services.AddSingleton<IdentityOperationHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<RegisterUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>>,
            RegisterUserHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<LoginUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>>,
            LoginUserHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<RefreshUserRequest<SchemataUser>, IdentityResult<ClaimsPrincipal>>,
            RefreshUserHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<GetUserProfileQuery<SchemataUser>, IdentityResult<ClaimsStore>>,
            GetUserProfileHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<ChangeUserEmailRequest<SchemataUser>, IdentityResult<Unit>>,
            ChangeUserEmailHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<ChangeUserPhoneRequest<SchemataUser>, IdentityResult<Unit>>,
            ChangeUserPhoneHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<ChangeUserPasswordRequest<SchemataUser>, IdentityResult<Unit>>,
            ChangeUserPasswordHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<ForgotUserPasswordRequest<SchemataUser>, IdentityResult<Unit>>,
            ForgotUserPasswordHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<ResetUserPasswordRequest<SchemataUser>, IdentityResult<Unit>>,
            ResetUserPasswordHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<ConfirmUserRequest<SchemataUser>, IdentityResult<Unit>>,
            ConfirmUserHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<SendUserConfirmationCodeRequest<SchemataUser>, IdentityResult<Unit>>,
            SendUserConfirmationCodeHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<GetUserAuthenticatorRequest<SchemataUser>, IdentityResult<AuthenticatorResponse>>,
            GetUserAuthenticatorHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<EnrollUserAuthenticatorRequest<SchemataUser>, IdentityResult<Unit>>,
            EnrollUserAuthenticatorHandler<SchemataUser>>();
        services.AddSingleton<
            IRequestHandler<DowngradeUserAuthenticatorRequest<SchemataUser>, IdentityResult<Unit>>,
            DowngradeUserAuthenticatorHandler<SchemataUser>>();
        services.AddSingleton<InProcessRequestDispatcher>();
        services.AddSingleton<IRequestDispatcher>(
            provider => provider.GetRequiredService<InProcessRequestDispatcher>());
        services.AddSingleton<IdentityHandler<SchemataUser>>();
        configure?.Invoke(services);
        _services = services.BuildServiceProvider();
        Handler = _services.GetRequiredService<IdentityHandler<SchemataUser>>();
    }

    internal IRequestDispatcher Dispatcher => _services.GetRequiredService<IRequestDispatcher>();

    internal IdentityHandler<SchemataUser> Handler { get; }

    internal Mock<IMailSender<SchemataUser>> Mail { get; }

    internal Mock<IMessageSender<SchemataUser>> Message { get; }

    internal Mock<SignInManager<SchemataUser>> SignIn { get; }

    internal IdentityOperationHandler<SchemataUser> Operations =>
        _services.GetRequiredService<IdentityOperationHandler<SchemataUser>>();

    internal Mock<SchemataUserManager<SchemataUser>> Users { get; }

    public void Dispose() {
        Users.Object.Dispose();
        _services.Dispose();
        _managerServices.Dispose();
    }
}
