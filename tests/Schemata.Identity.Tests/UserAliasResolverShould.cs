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
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Services;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Tests.Fixtures;
using Schemata.Validation.Skeleton.Advisors;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Unit")]
public sealed class UserAliasResolverShould
{
    [Fact]
    public void Disabled_Registration_Preserves_Username_And_Contacts() {
        var resolver = Resolver(UserNameAliasMode.Disabled);
        var request = new RegisterRequest {
            Username = "Alice", EmailAddress = "alice@example.com", PhoneNumber = "+14155550123", Password = "password",
        };

        var selected = resolver.ResolveRegistration(request);

        Assert.Equal(UserAliasType.UserName, selected.Type);
        Assert.Equal("Alice", selected.Value);
        Assert.Equal("Alice", selected.RegistrationName);
        Assert.Equal("alice@example.com", request.EmailAddress);
        Assert.Equal("+14155550123", request.PhoneNumber);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("phone")]
    [InlineData("split-phone")]
    [InlineData("type")]
    public void Disabled_Login_Rejects_Alternate_Key_Fields(string field) {
        var resolver = Resolver(UserNameAliasMode.Disabled);
        var request = new LoginRequest { Username = "alice", Password = "password" };
        switch (field) {
            case "email": request.EmailAddress = "alice@example.com"; break;
            case "phone": request.PhoneNumber = "+14155550123"; break;
            case "split-phone": request.PhoneRegion = "+1"; request.PhoneNumber = "4155550123"; break;
            default: request.UsernameType = "user-name"; break;
        }

        Assert.Throws<ValidationException>(() => resolver.ResolveLogin(request));
    }

    [Fact]
    public void Disabled_Login_Requires_Username() {
        var resolver = Resolver(UserNameAliasMode.Disabled);

        Assert.Throws<ValidationException>(() => resolver.ResolveLogin(new LoginRequest { Password = "password" }));
    }

    [Fact]
    public void Enabled_Prefers_Username_When_Contacts_Are_Present() {
        var resolver = Resolver(UserNameAliasMode.Enabled);
        var request = new LoginRequest {
            Username = "alice", EmailAddress = "alice@example.com", PhoneNumber = "+14155550123", Password = "password",
        };

        var selected = resolver.ResolveLogin(request);

        Assert.Equal(UserAliasType.UserName, selected.Type);
        Assert.Equal("alice", selected.Value);
    }

    [Fact]
    public void Enabled_Registration_Uses_Omitted_Username_Email_Alias() {
        var resolver = Resolver(UserNameAliasMode.Enabled);
        var request = new RegisterRequest { EmailAddress = "alice@example.com", Password = "password" };

        var selected = resolver.ResolveRegistration(request);

        Assert.Equal(UserAliasType.Email, selected.Type);
        Assert.Equal("alice@example.com", selected.Value);
        Assert.Equal("alice@example.com", selected.RegistrationName);
    }

    [Fact]
    public void Split_Phone_Registration_Assembles_E164_And_Assigns_Canonical_Digits() {
        var resolver = Resolver(UserNameAliasMode.Enabled);
        var request = new RegisterRequest { PhoneRegion = "+44", PhoneNumber = "1234567890123", Password = "password" };

        var selected = resolver.ResolveRegistration(request);

        Assert.Equal(UserAliasType.PhoneNumber, selected.Type);
        Assert.Equal("+441234567890123", selected.Value);
        Assert.Equal("441234567890123", selected.RegistrationName);
        Assert.Equal("+441234567890123", request.PhoneNumber);
    }

    [Theory]
    [InlineData("user-name", UserAliasType.UserName, "alice")]
    [InlineData("email", UserAliasType.Email, "alice@example.com")]
    [InlineData("phone-number", UserAliasType.PhoneNumber, "+14155550123")]
    public void Explicit_Selects_Only_The_Declared_Key(string type, UserAliasType expectedType, string expected) {
        var resolver = Resolver(UserNameAliasMode.Explicit);
        var request = new LoginRequest {
            Username = "alice", EmailAddress = "alice@example.com", PhoneNumber = "+14155550123",
            UsernameType = type, Password = "password",
        };

        var selected = resolver.ResolveLogin(request);

        Assert.Equal(expectedType, selected.Type);
        Assert.Equal(expected, selected.Value);
    }

    [Fact]
    public void Explicit_Registration_Allows_Unselected_Contact_Values() {
        var resolver = Resolver(UserNameAliasMode.Explicit);
        var request = new RegisterRequest {
            Username = "alice", UsernameType = "user-name", EmailAddress = "alice@example.com",
            PhoneRegion = "1", PhoneNumber = "4155550123", Password = "password",
        };

        var selected = resolver.ResolveRegistration(request);

        Assert.Equal("alice", selected.RegistrationName);
        Assert.Equal("+14155550123", request.PhoneNumber);
    }

    [Fact]
    public void Enabled_Rejects_Multiple_Automatic_Candidates() {
        var resolver = Resolver(UserNameAliasMode.Enabled);
        var request = new LoginRequest {
            EmailAddress = "alice@example.com", PhoneNumber = "+14155550123", Password = "password",
        };

        Assert.Throws<ValidationException>(() => resolver.ResolveLogin(request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Explicit_Rejects_Missing_Or_Unknown_Type(string? type) {
        var resolver = Resolver(UserNameAliasMode.Explicit);

        Assert.Throws<ValidationException>(() => resolver.ResolveLogin(new LoginRequest {
            Username = "alice", UsernameType = type, Password = "password",
        }));
    }

    [Theory]
    [InlineData("user-name")]
    [InlineData("email")]
    [InlineData("phone-number")]
    public void Explicit_Rejects_Missing_Selected_Value(string type) {
        var resolver = Resolver(UserNameAliasMode.Explicit);

        Assert.Throws<ValidationException>(() => resolver.ResolveLogin(new LoginRequest {
            UsernameType = type, Password = "password",
        }));
    }

    [Theory]
    [InlineData(UserNameAliasMode.Disabled)]
    [InlineData(UserNameAliasMode.Enabled)]
    public void Username_Type_Is_Rejected_Outside_Explicit_Mode(UserNameAliasMode mode) {
        var resolver = Resolver(mode);

        Assert.Throws<ValidationException>(() => resolver.ResolveLogin(new LoginRequest {
            Username = "alice", UsernameType = "user-name", Password = "password",
        }));
    }

    [Theory]
    [InlineData(UserNameAliasMode.Enabled)]
    [InlineData(UserNameAliasMode.Explicit)]
    public void Full_And_Split_Phone_Forms_Are_Rejected_As_Ambiguous(UserNameAliasMode mode) {
        var resolver = Resolver(mode);
        var request = new LoginRequest {
            Username = "alice", UsernameType = mode == UserNameAliasMode.Explicit ? "user-name" : null,
            PhoneRegion = "+1", PhoneNumber = "+14155550123", Password = "password",
        };

        Assert.Throws<ValidationException>(() => resolver.ResolveLogin(request));
    }

    [Fact]
    public void Split_Phone_Requires_Its_National_Number() {
        var resolver = Resolver(UserNameAliasMode.Enabled);

        Assert.Throws<ValidationException>(() => resolver.ResolveLogin(new LoginRequest {
            PhoneRegion = "+1", Password = "password",
        }));
    }

    private static ConfiguredUserAliasResolver Resolver(UserNameAliasMode mode) {
        return new(mode, new E164PhoneNumberNormalizer());
    }
}

[Trait("Layer", "Unit")]
public sealed class UserAliasLoginShould
{
    [Fact]
    public async Task Rejected_Explicit_Input_Never_Reaches_User_Lookup() {
        var store = new Mock<IUserStore<SchemataUser>>();
        var options = Options.Create(new IdentityOptions());
        var users = new Mock<SchemataUserManager<SchemataUser>>(MockBehavior.Loose, Mock.Of<IServiceProvider>(),
            store.Object, options, new PasswordHasher<SchemataUser>(), Array.Empty<IUserValidator<SchemataUser>>(),
            Array.Empty<IPasswordValidator<SchemataUser>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var resolver = new ConfiguredUserAliasResolver(UserNameAliasMode.Explicit, new E164PhoneNumberNormalizer());
        var sign = new SchemataSignInManager<SchemataUser>(users.Object, new HttpContextAccessor { HttpContext = context },
            Mock.Of<IUserClaimsPrincipalFactory<SchemataUser>>(), options, NullLogger<SignInManager<SchemataUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(), Mock.Of<IUserConfirmation<SchemataUser>>(), resolver, []);

        await Assert.ThrowsAsync<ValidationException>(() => sign.LoginAsync(new LoginRequest {
            EmailAddress = "alice@example.com", Password = "password",
        }, new ClaimsPrincipal(), new AdviceContext(services)));

        users.Verify(value => value.FindByNameAsync(It.IsAny<string>()), Times.Never);
        users.Verify(value => value.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        users.Verify(value => value.FindByPhoneAsync(It.IsAny<string>()), Times.Never);
        users.Verify(value => value.CheckPasswordAsync(It.IsAny<SchemataUser>(), It.IsAny<string>()), Times.Never);
    }
}

[Trait("Layer", "Unit")]
public sealed class AliasRequestValidationShould
{
    [Fact]
    public async Task Login_Validates_Before_Alias_Resolution() {
        var store = new Mock<IUserStore<SchemataUser>>();
        var options = Options.Create(new IdentityOptions());
        var users = new Mock<SchemataUserManager<SchemataUser>>(MockBehavior.Loose, Mock.Of<IServiceProvider>(),
            store.Object, options, new PasswordHasher<SchemataUser>(), Array.Empty<IUserValidator<SchemataUser>>(),
            Array.Empty<IPasswordValidator<SchemataUser>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        var validator = Validator<LoginRequest>(Operations.Authenticate);
        var resolver = new Mock<IUserAliasResolver>();
        using var services = new ServiceCollection().AddSingleton(validator.Object).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var sign = new SchemataSignInManager<SchemataUser>(users.Object, new HttpContextAccessor { HttpContext = context },
            Mock.Of<IUserClaimsPrincipalFactory<SchemataUser>>(), options, NullLogger<SignInManager<SchemataUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(), Mock.Of<IUserConfirmation<SchemataUser>>(), resolver.Object, []);

        await Assert.ThrowsAsync<ValidationException>(() => sign.LoginAsync(new LoginRequest {
            Username = "alice", Password = "password",
        }, new ClaimsPrincipal(), new AdviceContext(services)));

        validator.Verify(value => value.AdviseAsync(It.IsAny<AdviceContext>(), Operations.Authenticate,
            It.IsAny<LoginRequest>(), It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()), Times.Once);
        resolver.Verify(value => value.ResolveLogin(It.IsAny<LoginRequest>()), Times.Never);
        users.Verify(value => value.FindByNameAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Registration_Validates_Create_Before_Alias_Resolution() {
        var validator = Validator<RegisterRequest>(Operations.Create);
        var resolver = new Mock<IUserAliasResolver>();
        using var host = new IdentityHandlerTestHost(services => {
            services.AddSingleton(validator.Object);
            services.AddSingleton(resolver.Object);
        });

        await Assert.ThrowsAsync<ValidationException>(() => host.Handler.RegisterAsync(new RegisterRequest {
            Username = "alice", Password = "password",
        }, new ClaimsPrincipal()));

        validator.Verify(value => value.AdviseAsync(It.IsAny<AdviceContext>(), Operations.Create,
            It.IsAny<RegisterRequest>(), It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()), Times.Once);
        resolver.Verify(value => value.ResolveRegistration(It.IsAny<RegisterRequest>()), Times.Never);
        host.Users.Verify(value => value.CreateAsync(It.IsAny<SchemataUser>(), It.IsAny<string>()), Times.Never);
    }

    private static Mock<IValidationAdvisor<TRequest>> Validator<TRequest>(Operations operation) where TRequest : class {
        var validator = new Mock<IValidationAdvisor<TRequest>>();
        validator.SetupGet(value => value.Order).Returns(0);
        validator.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), operation, It.IsAny<TRequest>(),
                   It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()))
               .Callback((AdviceContext _, Operations _, TRequest _, IList<ErrorFieldViolation> errors, CancellationToken _) =>
                   errors.Add(new ErrorFieldViolation { Reason = "REJECTED_REQUEST" }))
               .ReturnsAsync(AdviseResult.Block);
        return validator;
    }
}
