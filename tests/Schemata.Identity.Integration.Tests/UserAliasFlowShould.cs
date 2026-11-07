using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Identity.Foundation.Services;
using Schemata.Identity.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class UserAliasFlowShould
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public async Task Enabled_Email_Registration_And_Login_Round_Trip() {
        using var factory = Factory(UserNameAliasMode.Enabled);
        using var client = factory.CreateClient();
        var registration = new RegisterRequest { EmailAddress = "alias@example.com", Password = "Valid-Pass123!" };

        using var registered = await client.PostAsJsonAsync("/Authenticate/Register", registration, Wire);
        using var login = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            EmailAddress = "alias@example.com", Password = "Valid-Pass123!",
        }, Wire);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = await FindByName(factory, "alias@example.com");
        Assert.Equal("alias@example.com", user.UserName);
        Assert.Equal("ALIAS@EXAMPLE.COM", user.NormalizedEmail);
        Assert.False(user.EmailConfirmed);
        Assert.False(user.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task Explicit_Split_Registration_And_Full_Phone_Login_Share_Canonical_Digits() {
        using var factory = Factory(UserNameAliasMode.Explicit);
        using var client = factory.CreateClient();
        using var registered = await client.PostAsJsonAsync("/Authenticate/Register", new RegisterRequest {
            UsernameType = "phone-number", PhoneRegion = "+44", PhoneNumber = "1234567890123", Password = "Valid-Pass123!",
        }, Wire);
        using var login = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            UsernameType = "phone-number", PhoneNumber = "+441234567890123", Password = "Valid-Pass123!",
        }, Wire);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = await FindByName(factory, "441234567890123");
        Assert.Equal("441234567890123", user.UserName);
        Assert.Equal("441234567890123", user.PhoneNumber);
    }

    [Fact]
    public async Task Duplicate_Final_Email_Rejects_The_Second_Registration() {
        using var factory = Factory(UserNameAliasMode.Enabled);
        using var client = factory.CreateClient();
        using var first = await client.PostAsJsonAsync("/Authenticate/Register", new RegisterRequest {
            Username = "first", EmailAddress = "shared@example.com", Password = "Valid-Pass123!",
        }, Wire);
        using var second = await client.PostAsJsonAsync("/Authenticate/Register", new RegisterRequest {
            Username = "second", EmailAddress = "SHARED@example.com", Password = "Valid-Pass123!",
        }, Wire);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
        Assert.Equal(1, await users.CountAsync(query => query.Where(user => user.NormalizedEmail == "SHARED@EXAMPLE.COM")));
    }

    [Fact]
    public async Task Unknown_And_Ambiguous_Email_Logins_Return_The_Same_Credential_Failure() {
        using var factory = Factory(UserNameAliasMode.Enabled);
        using var client = factory.CreateClient();
        await SeedDuplicateEmailUsers(factory);
        using var unknown = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            EmailAddress = "unknown@example.com", Password = "Valid-Pass123!",
        }, Wire);
        using var ambiguous = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            EmailAddress = "duplicate@example.com", Password = "Valid-Pass123!",
        }, Wire);

        Assert.Equal(unknown.StatusCode, ambiguous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ambiguous.StatusCode);
        Assert.Equal(unknown.Headers.WwwAuthenticate.ToString(), ambiguous.Headers.WwwAuthenticate.ToString());
        Assert.DoesNotContain("access_token", await unknown.Content.ReadAsStringAsync());
        var ambiguousBody = await ambiguous.Content.ReadAsStringAsync();
        Assert.DoesNotContain("access_token", ambiguousBody);
        Assert.DoesNotContain("duplicate-one", ambiguousBody);
        Assert.DoesNotContain("duplicate-two", ambiguousBody);
    }

    [Fact]
    public async Task Registration_Advisor_Field_Changes_Are_Normalized_By_The_Manager() {
        using var factory = Factory(UserNameAliasMode.Enabled, services => services.AddSingleton<IIdentityRegisterUserAdvisor, ChangeRegistrationFields>());
        using var client = factory.CreateClient();

        using var registered = await client.PostAsJsonAsync("/Authenticate/Register", new RegisterRequest {
            EmailAddress = "initial@example.com", Password = "Valid-Pass123!",
        }, Wire);

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var user = await FindByName(factory, "ChangedUser");
        Assert.Equal("changeduser", user.NormalizedUserName);
        Assert.Equal("CHANGED@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("15550001111", user.PhoneNumber);
        Assert.False(user.EmailConfirmed);
        Assert.False(user.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task Phone_Confirmation_Stores_Canonical_Digits_Through_The_Manager() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        using var registered = await client.PostAsJsonAsync("/Authenticate/Register", new RegisterRequest {
            Username = "phone-confirmation", Password = "Valid-Pass123!",
        }, Wire);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var user = await users.FindByNameAsync("phone-confirmation");
        Assert.NotNull(user);

        var code = await users.GenerateChangePhoneNumberTokenAsync(user, "+15550001111");
        var changed = await users.ChangePhoneNumberAsync(user, "+15550001111", code);

        Assert.True(changed.Succeeded);
        Assert.Equal("15550001111", user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task Disabled_Invalid_Contact_Phone_Returns_422_Without_Creating_A_User() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/Authenticate/Register", new RegisterRequest {
            Username = "invalid-contact", PhoneNumber = "15550001111", Password = "Valid-Pass123!",
        }, Wire);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_PHONE_NUMBER", body, StringComparison.Ordinal);
        Assert.Contains("phone_number", body, StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
        Assert.Equal(0, await users.CountAsync(query => query.Where(user => user.UserName == "invalid-contact")));
    }

    [Fact]
    public async Task Invalid_Phone_Confirmation_Returns_422_Without_Changing_The_Contact() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        using var registered = await client.PostAsJsonAsync("/Authenticate/Register", new RegisterRequest {
            Username = "invalid-confirmation", Password = "Valid-Pass123!", UseCookies = true,
        }, Wire);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        string code;
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var user = await users.FindByNameAsync("invalid-confirmation");
            Assert.NotNull(user);
            code = await users.GenerateChangePhoneNumberTokenAsync(user, "15550001111");
        }

        using var response = await client.GetAsync(
            $"/Authenticate/Confirm?phone_number=15550001111&code={Uri.EscapeDataString(code)}");

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        using var verification = factory.Services.CreateScope();
        var manager = verification.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var persisted = await manager.FindByNameAsync("invalid-confirmation");
        Assert.NotNull(persisted);
        Assert.Null(persisted.PhoneNumber);
        Assert.False(persisted.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task Disabled_Login_Rejects_An_Email_Alias() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            EmailAddress = "alias@example.com", Password = "Valid-Pass123!",
        }, Wire);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    private static WebAppFactory Factory(UserNameAliasMode mode, Action<IServiceCollection>? configure = null) {
        return new WebAppFactory().WithServices(services => {
            services.Replace(ServiceDescriptor.Scoped<IUserAliasResolver>(provider =>
                new ConfiguredUserAliasResolver(mode, provider.GetRequiredService<IPhoneNumberNormalizer>())));
            configure?.Invoke(services);
        });
    }

    private static async Task<SchemataUser> FindByName(WebAppFactory factory, string name) {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var user = await users.FindByNameAsync(name);
        Assert.NotNull(user);
        return user;
    }

    private static async Task SeedDuplicateEmailUsers(WebAppFactory factory) {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
        foreach (var name in new[] { "duplicate-one", "duplicate-two" }) {
            await users.AddAsync(new SchemataUser {
                Name = name, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = "duplicate@example.com",
                NormalizedEmail = "DUPLICATE@EXAMPLE.COM", SecurityStamp = Guid.NewGuid().ToString("n"),
                Timestamp = Guid.NewGuid(),
            });
        }
        await users.CommitAsync();
    }

    private sealed class ChangeRegistrationFields : IIdentityRegisterUserAdvisor
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext context,
            SchemataUser user,
            RegisterRequest request,
            IHandledResponse<IdentityResult<ClaimsPrincipal>> handled,
            CancellationToken ct = default
        ) {
            user.UserName = "ChangedUser";
            user.Email = "changed@example.com";
            user.PhoneNumber = "+15550001111";
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
