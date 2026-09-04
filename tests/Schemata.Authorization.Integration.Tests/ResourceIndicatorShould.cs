using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class ResourceIndicatorShould
{
    private const string RedirectUri = "https://localhost/callback";
    private const string Challenge   = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    /// <summary>The RFC 7636 Appendix B verifier matching the challenge.</summary>
    private const string Verifier    = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    /// <summary>The RFC 8707 §2.1 Figure 2 grant pair.</summary>
    private const string Calendar  = "https://cal.example.com/";
    private const string Contacts  = "https://contacts.example.com/";
    private const string Foreign   = "https://foreign.example.com/";

    private const string Issuer = "https://localhost";

    [Fact]
    public async Task Exchange_With_The_Granted_Resource_Set_Succeeds() {
        using var factory = New_Authenticated_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(InteractionAuthenticationHandler.SchemeName);

        var code = await Approve(client, Calendar, Contacts);

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
            new("code_verifier", Verifier),
            new("resource", Calendar),
            new("resource", Contacts),
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(token.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task Exchange_With_A_Different_Resource_Set_Is_InvalidTarget() {
        using var factory = New_Authenticated_Factory();
        var client = factory.CreateClient();

        var code = await Mint_Code(factory, [Calendar, Contacts], "openid offline_access");

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
            new("resource", Calendar),
            new("resource", Foreign),
        }));

        await Assert_InvalidTarget(response);
    }

    [Fact]
    public async Task Exchange_With_Resources_Mints_An_Audience_Array() {
        using var factory = New_Jwt_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(InteractionAuthenticationHandler.SchemeName);

        var code = await Approve(client, Calendar, Contacts);

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
            new("code_verifier", Verifier),
            new("resource", Calendar),
            new("resource", Contacts),
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token   = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var payload = Payload(token.GetProperty("access_token").GetString()!);
        Assert.True(payload.TryGetProperty(Claims.Audience, out var aud));
        Assert.Equal(JsonValueKind.Array, aud.ValueKind);
        Assert.Equal(new[] { Calendar, Contacts }, aud.EnumerateArray().Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task Exchange_Without_Resources_Defaults_The_Audience_To_The_Issuer() {
        using var factory = New_Jwt_Factory();
        var client = factory.CreateClient();

        var code = await Mint_Code(factory, [], "openid offline_access");

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token   = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var payload = Payload(token.GetProperty("access_token").GetString()!);
        Assert.True(payload.TryGetProperty(Claims.Audience, out var aud));
        Assert.Equal(JsonValueKind.String, aud.ValueKind);
        Assert.Equal(Issuer, aud.GetString());
    }

    [Fact]
    public async Task Id_Token_Audience_Remains_The_Client_With_Resources() {
        using var factory = New_Authenticated_Factory();
        var client = factory.CreateClient();

        var code = await Mint_Code(factory, [Calendar, Contacts], "openid offline_access");

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
            new("resource", Calendar),
            new("resource", Contacts),
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pair    = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var payload = Payload(pair.GetProperty("id_token").GetString()!);
        Assert.True(payload.TryGetProperty(Claims.Audience, out var aud));
        Assert.Equal("code-client", aud.GetString());
    }

    [Fact]
    public async Task Introspection_Echoes_The_Audience_Array() {
        using var factory = New_Authenticated_Factory();
        var client = factory.CreateClient();

        var code = await Mint_Code(factory, [Calendar, Contacts], "openid offline_access");
        var exchange = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
            new("resource", Calendar),
            new("resource", Contacts),
        }));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);

        var pair = JsonDocument.Parse(await exchange.Content.ReadAsStreamAsync()).RootElement;

        var response = await client.PostAsync(
            "/connect/introspect",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["token"]         = pair.GetProperty("access_token").GetString()!,
                ["client_id"]     = "introspect-client",
                ["client_secret"] = "introspect-secret",
            }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var introspection = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(introspection.GetProperty("active").GetBoolean());
        Assert.Equal(JsonValueKind.Array, introspection.GetProperty("aud").ValueKind);
        Assert.Equal(
            new[] { Calendar, Contacts },
            introspection.GetProperty("aud").EnumerateArray().Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task Refresh_With_A_Subset_Of_The_Granted_Resources_Succeeds() {
        using var factory = New_Authenticated_Factory();
        var client = factory.CreateClient();

        var code = await Mint_Code(factory, [Calendar, Contacts], "openid offline_access");
        var exchange = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
        }));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);

        var pair = JsonDocument.Parse(await exchange.Content.ReadAsStreamAsync()).RootElement;
        var refreshToken = pair.GetProperty("refresh_token").GetString();

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.RefreshToken),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("refresh_token", refreshToken!),
            new("resource", Contacts),
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(token.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task Refresh_With_A_Foreign_Resource_Is_InvalidTarget() {
        using var factory = New_Authenticated_Factory();
        var client = factory.CreateClient();

        var code = await Mint_Code(factory, [Calendar, Contacts], "openid offline_access");
        var exchange = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
        }));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);

        var pair = JsonDocument.Parse(await exchange.Content.ReadAsStreamAsync()).RootElement;

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.RefreshToken),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("refresh_token", pair.GetProperty("refresh_token").GetString()!),
            new("resource", Foreign),
        }));

        await Assert_InvalidTarget(response);
    }

    [Fact]
    public async Task Authorize_With_A_Fragment_Resource_Is_InvalidTarget() {
        using var factory = New_Authenticated_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/connect/authorize?client_id=browser-client"
            + "&redirect_uri=https%3A%2F%2Flocalhost%2Fcallback"
            + "&response_type=code&state=xyz"
            + "&code_challenge=" + Challenge
            + "&code_challenge_method=S256"
            + "&resource=" + Uri.EscapeDataString("https://cal.example.com/#row"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal(OAuthErrors.InvalidTarget, query["error"]);
    }

    private static async Task Assert_InvalidTarget(HttpResponseMessage response) {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidTarget, error.GetProperty("error").GetString());
    }

    /// <summary>
    ///     Runs the interactive legs: authorize with the given resources, approve at the
    ///     interaction endpoint, and return the authorization code from the callback.
    /// </summary>
    private static async Task<string> Approve(HttpClient client, params string[] resources) {
        var url = "/connect/authorize?client_id=code-client"
                + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                + "&response_type=code&state=xyz"
                + "&code_challenge=" + Challenge
                + "&code_challenge_method=S256";
        foreach (var resource in resources) {
            url += "&resource=" + Uri.EscapeDataString(resource);
        }

        var authorize = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);

        var interaction = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query);
        var approve = await client.PostAsync(
            "/connect/interact",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = interaction[Parameters.Code]!,
                ["code_type"] = interaction[Parameters.CodeType]!,
            }));
        var approveBody = await approve.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Found == approve.StatusCode, approveBody);

        var callback = HttpUtility.ParseQueryString(approve.Headers.Location!.Query);
        return callback[Parameters.Code]!;
    }

    /// <summary>
    ///     Issues a code through the sign-in service the way the interaction approval path does,
    ///     so the payload reconstruction carries the granted resources.
    /// </summary>
    private static async Task<string> Mint_Code(WebAppFactory factory, string[] resources, string scopes) {
        using var scope  = factory.Services.CreateScope();
        var       signIn = scope.ServiceProvider.GetRequiredService<IAuthorizationSignInService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new List<Claim> {
                new(IdentityClaims.Subject, "users/u-1"),
                new(Claims.ClientId, "code-client"),
            }, "test"));

        var properties = new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.Scope]        = scopes,
            [Properties.RedirectUri]  = RedirectUri,
        };
        if (resources.Length > 0) {
            properties[Properties.Resources] = string.Join(" ", resources);
        }

        var response = await signIn.IssueAsync(principal, properties, AuthorizationSignInResponseKind.Callback);
        return response.Callback!.Parameters[Parameters.Code]!;
    }

    private static HttpRequestMessage Token(List<KeyValuePair<string, string>> fields) {
        return new(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent(fields) };
    }

    /// <summary>Decodes a JWT payload segment into its raw JSON so scalar and array shapes are visible.</summary>
    private static JsonElement Payload(string jwt) {
        using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private static WebAppFactory New_Authenticated_Factory() {
        return new WebAppFactory()
            .WithEnvironment("Authenticated")
            .WithServices(Configure_Interaction_Authentication);
    }

    /// <summary>A factory issuing plain signed JWT access tokens so tests can parse their payloads.</summary>
    private static WebAppFactory New_Jwt_Factory() {
        return new WebAppFactory().WithEnvironment("Authenticated").WithServices(services => {
            Configure_Interaction_Authentication(services);
            services.PostConfigure<SchemataAuthorizationOptions>(o => o.AccessTokenFormat = TokenFormats.Jwt);
        });
    }

    private static void Configure_Interaction_Authentication(IServiceCollection services) {
        services.AddAuthentication(InteractionAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, InteractionAuthenticationHandler>(
                    InteractionAuthenticationHandler.SchemeName, _ => { });
    }

    /// <summary>Authenticates the resource owner for the interaction approval POST.</summary>
    private sealed class InteractionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                   encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ManagementTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != SchemeName) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
            ], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new(principal, SchemeName)));
        }
    }
}
