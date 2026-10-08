using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

[Trait("Layer", "Integration")]
public class LoginShould
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Password_Only_Issues_One_Bearer_Response_And_Optional_Session_Cookie(bool cookies) {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var name = await CreateUser(factory, false);
        using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            Username = name, Password = "Valid-Pass123!", UseCookies = cookies,
        }, Wire);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ticket = await ReadTicket(factory, response);
        Assert.Equal(AuthenticationContextClasses.Password, ticket.Principal.FindFirst("acr")?.Value);
        Assert.Equal("[\"pwd\"]", Assert.Single(ticket.Principal.FindAll("amr")).Value);
        var headers = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];
        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var applicationName = cookieOptions.Get(IdentityConstants.ApplicationScheme).Cookie.Name + "=";
        var rememberedName = cookieOptions.Get(IdentityConstants.TwoFactorRememberMeScheme).Cookie.Name + "=";
        Assert.Equal(cookies, headers.Any(h => h.StartsWith(applicationName, StringComparison.Ordinal)));
        Assert.DoesNotContain(headers, h => h.StartsWith(rememberedName, StringComparison.Ordinal));
        if (cookies) Assert.DoesNotContain(headers, h => h.StartsWith(applicationName, StringComparison.Ordinal) && h.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Correct_Password_Without_Second_Factor_Produces_Only_Pending_Identity() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var name = await CreateUser(factory, true);
        using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest { Username = name, Password = "Valid-Pass123!" }, Wire);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
        var headers = response.Headers.GetValues("Set-Cookie").ToArray();
        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        Assert.Contains(headers, h => h.StartsWith(cookieOptions.Get(IdentityConstants.TwoFactorUserIdScheme).Cookie.Name + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(headers, h => h.StartsWith(cookieOptions.Get(IdentityConstants.ApplicationScheme).Cookie.Name + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recovery_Code_Completes_Multifactor_Once_And_Cannot_Be_Replayed() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var name = await CreateUser(factory, true);
        string code;
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            code = Assert.Single((await users.GenerateNewTwoFactorRecoveryCodesAsync((await users.FindByNameAsync(name))!, 1))!);
        }
        var request = new LoginRequest { Username = name, Password = "Valid-Pass123!", TwoFactorRecoveryCode = code, UseCookies = true };
        using var response = await client.PostAsJsonAsync("/Authenticate/Login", request, Wire);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ticket = await ReadTicket(factory, response);
        Assert.Equal(AuthenticationContextClasses.Multifactor, ticket.Principal.FindFirst("acr")?.Value);
        Assert.Equal("[\"pwd\",\"otp\",\"mfa\"]", Assert.Single(ticket.Principal.FindAll("amr")).Value);
        using var replay = await client.PostAsJsonAsync("/Authenticate/Login", request, Wire);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.DoesNotContain("access_token", await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_Advisor_Veto_Prevents_Final_Credentials_After_Valid_Recovery() {
        using var root = new WebAppFactory();
        using var factory = root.WithServices(services => services.AddScoped<IIdentityLoginAdvisor, DenyLogin>());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var name = await CreateUser(factory, true);
        string code;
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            code = Assert.Single((await users.GenerateNewTwoFactorRecoveryCodesAsync((await users.FindByNameAsync(name))!, 1))!);
        }
        using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            Username = name, Password = "Valid-Pass123!", TwoFactorRecoveryCode = code, UseCookies = true,
        }, Wire);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
        var headers = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        var applicationName = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                                     .Get(IdentityConstants.ApplicationScheme).Cookie.Name + "=";
        Assert.DoesNotContain(headers, h => h.StartsWith(applicationName, StringComparison.Ordinal));
        using var verification = factory.Services.CreateScope();
        var manager = verification.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        Assert.Equal(0, await manager.CountRecoveryCodesAsync((await manager.FindByNameAsync(name))!));
    }

    [Theory]
    [InlineData(AuthenticationContextClasses.Password, AuthenticationContextClasses.Password)]
    [InlineData(AuthenticationContextClasses.Multifactor + " " + AuthenticationContextClasses.Password, AuthenticationContextClasses.Multifactor)]
    [InlineData("urn:unknown", AuthenticationContextClasses.Multifactor)]
    public async Task Authenticator_Completes_Platform_Flow_With_Observer_Evidence(string requested, string expected) {
        using var root = new WebAppFactory();
        using var factory = root.WithServices(services => services.AddScoped<IHostSignInObserver, SessionObserver>());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var name = await CreateUser(factory, true);
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var user = (await users.FindByNameAsync(name))!;
            var store = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
            await ((IUserAuthenticatorKeyStore<SchemataUser>)store).SetAuthenticatorKeyAsync(user, "JBSWY3DPEHPK3PXP", CancellationToken.None);
        }
        using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            Username = name, Password = "Valid-Pass123!", TwoFactorCode = AuthenticatorCode(), UseCookies = true, AcrValues = requested,
        }, Wire);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bearer = await ReadTicket(factory, response);
        Assert.Equal(expected, bearer.Principal.FindFirst("acr")?.Value);
        Assert.Equal("[\"pwd\",\"otp\",\"mfa\"]", Assert.Single(bearer.Principal.FindAll("amr")).Value);
        Assert.Equal("observed-session", Assert.Single(bearer.Principal.FindAll("sid")).Value);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var prefix = options.Cookie.Name + "=";
        var cookie = response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(prefix, StringComparison.Ordinal));
        var protectedTicket = Uri.UnescapeDataString(cookie.Split(';')[0][prefix.Length..]);
        var application = Assert.IsType<AuthenticationTicket>(options.TicketDataFormat.Unprotect(protectedTicket));
        Assert.Equal("observed-session", Assert.Single(application.Principal.FindAll("sid")).Value);
        Assert.False(application.Properties.IsPersistent);
    }

    [Fact]
    public async Task Remembered_Device_Bypasses_Unused_Code_Without_Claiming_Fresh_Multifactor() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var name = await CreateUser(factory, true);
        string remembered;
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var user = (await users.FindByNameAsync(name))!;
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var sign = scope.ServiceProvider.GetRequiredService<SignInManager<SchemataUser>>();
            sign.Context = context;
            await sign.RememberTwoFactorClientAsync(user);
            remembered = context.Response.Headers.SetCookie.Single()!.Split(';')[0];
        }
        client.DefaultRequestHeaders.Add("Cookie", remembered);
        using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            Username = name, Password = "Valid-Pass123!", TwoFactorCode = "not-a-code", AcrValues = AuthenticationContextClasses.Multifactor,
        }, Wire);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ticket = await ReadTicket(factory, response);
        Assert.Equal("[\"pwd\"]", Assert.Single(ticket.Principal.FindAll("amr")).Value);
        Assert.Equal(AuthenticationContextClasses.Password, ticket.Principal.FindFirst("acr")?.Value);
    }

    [Fact]
    public async Task Invalid_Authenticator_Advances_Lockout_And_Locked_User_Cannot_Sign_In() {
        using var root = new WebAppFactory();
        using var factory = root.WithServices(services => services.Configure<IdentityOptions>(o => o.Lockout.MaxFailedAccessAttempts = 2));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var name = await CreateUser(factory, true);
        for (var attempt = 0; attempt < 2; attempt++) {
            using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
                Username = name, Password = "Valid-Pass123!", TwoFactorCode = "invalid",
            }, Wire);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
        }
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByNameAsync(name))!));
        using var locked = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            Username = name, Password = "Valid-Pass123!",
        }, Wire);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.DoesNotContain("access_token", await locked.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.InternalServerError)]
    public async Task Final_Gate_Failure_Produces_No_Application_Or_Bearer_Credentials(bool observerFailure, HttpStatusCode expected) {
        using var root = new WebAppFactory();
        using var factory = root.WithServices(services => {
            if (observerFailure) services.AddScoped<IHostSignInObserver, FailingObserver>();
            else services.AddScoped<IIdentityLoginAdvisor, ChallengeLogin>();
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var name = await CreateUser(factory, false);
        using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            Username = name, Password = "Valid-Pass123!", UseCookies = true,
        }, Wire);
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var headers) ? headers : [];
        var prefix = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                            .Get(IdentityConstants.ApplicationScheme).Cookie.Name + "=";
        Assert.DoesNotContain(cookies, h => h.StartsWith(prefix, StringComparison.Ordinal));
    }

    private sealed class ChallengeLogin : IIdentityLoginAdvisor
    {
        public int Order => 0;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, SchemataUser user, LoginRequest request, CancellationToken ct = default) {
            context.Set(Schemata.Identity.Skeleton.IdentityResult<ClaimsPrincipal>.Challenge());
            return Task.FromResult(AdviseResult.Handle);
        }
    }

    private sealed class FailingObserver : IHostSignInObserver
    {
        public Task OnSigningInAsync(ClaimsPrincipal principal, ClaimsPrincipal? prior, CancellationToken ct)
            => throw new InvalidOperationException("Observer rejected sign-in.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Request_Handled_Result_Controls_Issuance_And_Observers(bool success) {
        using var root = new WebAppFactory();
        using var factory = root.WithServices(services => {
            services.AddSingleton<IIdentityRequestAdvisor<LoginRequest>>(new HandleLogin(success));
            services.AddScoped<IHostSignInObserver, SessionObserver>();
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var response = await client.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            Username = "not-a-stored-user", Password = "not-a-password", UseCookies = true,
        }, Wire);
        Assert.Equal(success ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        if (success) {
            var ticket = await ReadTicket(factory, response);
            Assert.Equal("users/external", ticket.Principal.FindFirst("sub")?.Value);
            Assert.Equal("observed-session", Assert.Single(ticket.Principal.FindAll("sid")).Value);
        } else {
            Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }

    private sealed class HandleLogin(bool success) : IIdentityRequestAdvisor<LoginRequest>
    {
        public int Order => 0;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, LoginRequest request, IdentityOperation operation,
                                            ClaimsPrincipal principal, CancellationToken ct = default) {
            context.Set(success
                ? Schemata.Identity.Skeleton.IdentityResult<ClaimsPrincipal>.Success(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "users/external")], "external")))
                : Schemata.Identity.Skeleton.IdentityResult<ClaimsPrincipal>.Challenge());
            return Task.FromResult(AdviseResult.Handle);
        }
    }

    private static string AuthenticatorCode() {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(new byte[] { 72, 101, 108, 108, 111, 33, 222, 173, 190, 239 }, counter);
        var offset = hash[^1] & 15;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (value % 1000000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private sealed class SessionObserver : IHostSignInObserver
    {
        public Task OnSigningInAsync(ClaimsPrincipal principal, ClaimsPrincipal? incoming, CancellationToken ct = default) {
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("sid", "observed-session"));
            return Task.CompletedTask;
        }
    }

    private static async Task<string> CreateUser(WebAppFactory factory, bool twoFactor) {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var user = new SchemataUser { UserName = "login-" + Guid.NewGuid().ToString("N"), LockoutEnabled = true };
        Assert.True((await users.CreateAsync(user, "Valid-Pass123!")).Succeeded);
        if (twoFactor) {
            Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        }
        return user.UserName;
    }

    private static async Task<AuthenticationTicket> ReadTicket(WebAppFactory factory, HttpResponseMessage response) {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("access_token").GetString()!;
        var options = factory.Services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(IdentityConstants.BearerScheme);
        return Assert.IsType<AuthenticationTicket>(options.BearerTokenProtector.Unprotect(token));
    }

    private sealed class DenyLogin : IIdentityLoginAdvisor
    {
        public int Order => 0;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, SchemataUser user, LoginRequest request, CancellationToken ct = default)
            => Task.FromResult(AdviseResult.Block);
    }
}
