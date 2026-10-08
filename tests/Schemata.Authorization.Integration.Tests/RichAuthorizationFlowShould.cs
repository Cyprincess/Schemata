using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class RichAuthorizationFlowShould
{
    /// <summary>The RFC 7636 Appendix B challenge for the fixed verifier.</summary>
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string Verifier  = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    private const string RedirectUri = "https://localhost/callback";

    private const string Details   = """[{"type":"payment_initiation","actions":["list"],"institutions":["cb"]}]""";
    private const string UnknownType = """[{"type":"account_information","actions":["list"]}]""";

    /// <summary>The broad grant the code carries; narrowing must never rewrite it.</summary>
    private const string Broad     = """[{"type":"payment_initiation","actions":["list","read"],"institutions":["cb","pb"]}]""";
    private const string Narrowed  = """[{"type":"payment_initiation","actions":["list"]}]""";
    private const string Expansion = """[{"type":"payment_initiation","actions":["list","read","execute"]}]""";

    private readonly WebAppFactory _factory = new WebAppFactory().WithEnvironment("Rar");

    [Fact]
    public async Task Accept_The_Parameter_Under_The_Feature() {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize(Details));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Null(location[Parameters.Error]);
        Assert.False(string.IsNullOrWhiteSpace(location[Parameters.Code]));
    }

    [Fact]
    public async Task Reject_An_Unregistered_Type_Under_The_Feature() {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize(UnknownType));

        // Issue #135: a rejection after client/redirect validation finalizes through the
        // authorization callback, preserving the validated redirect.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var query = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, query[Parameters.Error]);
    }

    [Fact]
    public async Task Ignore_The_Parameter_Without_The_Feature() {
        using var factory = new WebAppFactory();
        var       client  = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize(Details));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Null(location[Parameters.Error]);
        Assert.False(string.IsNullOrWhiteSpace(location[Parameters.Code]));
    }

    [Fact]
    public async Task Carry_Only_The_Validated_Set_Through_The_Interaction() {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize(Details));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var reference = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query)[Parameters.Code];
        Assert.NotNull(reference);

        var request = await ReadInteractionRequest(_factory, reference);
        Assert.NotNull(request.AuthorizationDetails);
        using var document = JsonDocument.Parse(request.AuthorizationDetails);
        AssertDetails(document.RootElement, ["list"], ["cb"]);
    }

    [Fact]
    public async Task Drop_The_Raw_Parameter_From_The_Interaction_Without_The_Feature() {
        using var factory = new WebAppFactory();
        var       client  = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(Authorize(Details));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var reference = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query)[Parameters.Code];
        Assert.NotNull(reference);

        var request = await ReadInteractionRequest(factory, reference);
        Assert.Null(request.AuthorizationDetails);
    }

    [Fact]
    public async Task Exchange_With_Narrower_Details_Returns_The_Actual_Set_And_Preserves_The_Grant() {
        using var factory = Rar(jwt: true);
        var       client  = factory.CreateClient();

        var code      = await MintCode(factory, Broad);
        var persisted = await ReadCodePayload(factory, code);
        Assert.Contains("read", persisted);

        using var response = await client.SendAsync(Token(ExchangeForm(code, Narrowed)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        AssertDetails(root.GetProperty("authorization_details"), ["list"], ["cb", "pb"]);

        var access = root.GetProperty("access_token").GetString();
        Assert.NotNull(access);
        AssertDetails(Payload(access).GetProperty(Claims.AuthorizationDetails), ["list"], ["cb", "pb"]);

        // RFC 9396 §6.1: the exchange narrows the new token, never the grant the code carries.
        Assert.Equal(persisted, await ReadCodePayload(factory, code));
    }

    [Fact]
    public async Task Exchange_With_Expanded_Details_Is_InvalidAuthorizationDetails() {
        using var factory = Rar();
        var       client  = factory.CreateClient();
        var       code    = await MintCode(factory, Broad);

        using var response = await client.SendAsync(Token(ExchangeForm(code, Expansion)));

        await AssertInvalidAuthorizationDetails(response);
    }

    [Fact]
    public async Task Refresh_Narrowing_Is_Retained_By_A_Later_Omission() {
        using var factory = Rar(jwt: true);
        var       client  = factory.CreateClient();
        var       code    = await MintCode(factory, Broad);

        using var exchange = await client.SendAsync(Token(ExchangeForm(code, null)));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var issued = JsonDocument.Parse(await exchange.Content.ReadAsStreamAsync()).RootElement;
        AssertDetails(issued.GetProperty("authorization_details"), ["list", "read"], ["cb", "pb"]);
        var broad = issued.GetProperty("refresh_token").GetString();
        Assert.NotNull(broad);

        using var narrowed = await client.SendAsync(Token(RefreshForm(broad, Narrowed)));
        Assert.Equal(HttpStatusCode.OK, narrowed.StatusCode);
        var narrowedRoot = JsonDocument.Parse(await narrowed.Content.ReadAsStreamAsync()).RootElement;
        AssertDetails(narrowedRoot.GetProperty("authorization_details"), ["list"], ["cb", "pb"]);
        var narrowedAccess = narrowedRoot.GetProperty("access_token").GetString();
        Assert.NotNull(narrowedAccess);
        AssertDetails(Payload(narrowedAccess).GetProperty(Claims.AuthorizationDetails), ["list"], ["cb", "pb"]);
        var successor = narrowedRoot.GetProperty("refresh_token").GetString();
        Assert.NotNull(successor);

        // RFC 9396 §6.1: an omitted parameter adopts the grant the presented token carries —
        // the prior actual narrowing, never the original broad authorization request.
        using var retained = await client.SendAsync(Token(RefreshForm(successor, null)));
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        var retainedRoot = JsonDocument.Parse(await retained.Content.ReadAsStreamAsync()).RootElement;
        AssertDetails(retainedRoot.GetProperty("authorization_details"), ["list"], ["cb", "pb"]);
        var retainedAccess = retainedRoot.GetProperty("access_token").GetString();
        Assert.NotNull(retainedAccess);
        AssertDetails(Payload(retainedAccess).GetProperty(Claims.AuthorizationDetails), ["list"], ["cb", "pb"]);
    }

    [Fact]
    public async Task Refresh_With_Expanded_Details_Is_InvalidAuthorizationDetails() {
        using var factory = Rar();
        var       client  = factory.CreateClient();
        var       code    = await MintCode(factory, Broad);

        using var exchange = await client.SendAsync(Token(ExchangeForm(code, null)));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var refresh = JsonDocument.Parse(await exchange.Content.ReadAsStreamAsync()).RootElement
                                  .GetProperty("refresh_token").GetString();
        Assert.NotNull(refresh);

        using var response = await client.SendAsync(Token(RefreshForm(refresh, Expansion)));

        await AssertInvalidAuthorizationDetails(response);
    }

    [Fact]
    public async Task Refresh_With_An_Empty_Set_Is_Preserved_Across_A_Later_Omission() {
        using var factory = Rar(jwt: true);
        var       client  = factory.CreateClient();
        var       code    = await MintCode(factory, Broad);

        using var exchange = await client.SendAsync(Token(ExchangeForm(code, null)));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var broad = JsonDocument.Parse(await exchange.Content.ReadAsStreamAsync()).RootElement
                                .GetProperty("refresh_token").GetString();
        Assert.NotNull(broad);

        using var emptied = await client.SendAsync(Token(RefreshForm(broad, "[]")));
        Assert.Equal(HttpStatusCode.OK, emptied.StatusCode);
        var emptiedRoot = JsonDocument.Parse(await emptied.Content.ReadAsStreamAsync()).RootElement;
        AssertEmptyDetails(emptiedRoot.GetProperty("authorization_details"));
        var emptiedAccess = emptiedRoot.GetProperty("access_token").GetString();
        Assert.NotNull(emptiedAccess);
        AssertEmptyDetails(Payload(emptiedAccess).GetProperty(Claims.AuthorizationDetails));
        var successor = emptiedRoot.GetProperty("refresh_token").GetString();
        Assert.NotNull(successor);

        // IdentityModel emits no claim for an empty JSON array; TokenService restores the
        // verified marker, so the omitted parameter still adopts the explicit empty set.
        using var retained = await client.SendAsync(Token(RefreshForm(successor, null)));
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
        var retainedRoot = JsonDocument.Parse(await retained.Content.ReadAsStreamAsync()).RootElement;
        AssertEmptyDetails(retainedRoot.GetProperty("authorization_details"));
        var retainedAccess = retainedRoot.GetProperty("access_token").GetString();
        Assert.NotNull(retainedAccess);
        AssertEmptyDetails(Payload(retainedAccess).GetProperty(Claims.AuthorizationDetails));
    }


    [Fact]
    public async Task Introspection_Echoes_The_Actual_Details() {
        using var factory = Rar();
        var       client  = factory.CreateClient();
        var       code    = await MintCode(factory, Broad);

        using var exchange = await client.SendAsync(Token(ExchangeForm(code, Narrowed)));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var access = JsonDocument.Parse(await exchange.Content.ReadAsStreamAsync()).RootElement
                                 .GetProperty("access_token").GetString();
        Assert.NotNull(access);

        using var response = await client.PostAsync(
            "/connect/introspect",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["token"]         = access,
                ["client_id"]     = "introspect-client",
                ["client_secret"] = "introspect-secret",
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(body.GetProperty("active").GetBoolean());
        AssertDetails(body.GetProperty("authorization_details"), ["list"], ["cb", "pb"]);
    }

    [Fact]
    public async Task Ignore_The_Token_Request_Parameter_Without_The_Feature() {
        using var factory = new WebAppFactory().WithServices(
            services => services.PostConfigure<SchemataAuthorizationOptions>(
                o => o.AccessTokenFormat = TokenFormats.Jwt));
        var client = factory.CreateClient();
        var code   = await MintCode(factory, null);

        using var response = await client.SendAsync(Token(ExchangeForm(code, Narrowed)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.False(root.TryGetProperty("authorization_details", out _));
        var access = root.GetProperty("access_token").GetString();
        Assert.NotNull(access);
        Assert.False(Payload(access).TryGetProperty(Claims.AuthorizationDetails, out _));
    }

    private static string Authorize(string details) {
        return "/connect/authorize?client_id=code-client"
             + "&redirect_uri=https%3A%2F%2Flocalhost%2Fcallback"
             + "&response_type=code"
             + "&scope=openid"
             + "&code_challenge=" + Challenge
             + "&code_challenge_method=S256"
             + "&authorization_details=" + Uri.EscapeDataString(details);
    }

    private static WebAppFactory Rar(bool jwt = false) {
        var factory = new WebAppFactory().WithEnvironment("Rar");
        return jwt
            ? factory.WithServices(
                services => services.PostConfigure<SchemataAuthorizationOptions>(
                    o => o.AccessTokenFormat = TokenFormats.Jwt))
            : factory;
    }

    /// <summary>
    ///     Issues a code through the sign-in service the way the interaction approval path does,
    ///     so the payload reconstruction carries the granted authorization details.
    /// </summary>
    private static async Task<string> MintCode(WebAppFactory factory, string? details) {
        using var scope  = factory.Services.CreateScope();
        var       signIn = scope.ServiceProvider.GetRequiredService<IAuthorizationSignInService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new List<Claim> {
                new(IdentityClaims.Subject, "users/u-1"),
                new(Claims.ClientId, "code-client"),
            }, "test"));

        var properties = new Dictionary<string, string?> {
            [Properties.GrantType]            = GrantTypes.AuthorizationCode,
            [Properties.ResponseType]         = ResponseTypes.Code,
            [Properties.Scope]                = "openid offline_access",
            [Properties.RedirectUri]          = RedirectUri,
            [Properties.GrantProfile]         = GrantProfiles.OpenIdConnect,
            [Properties.AuthorizationDetails] = details,
        };

        var response = await signIn.IssueAsync(principal, properties, AuthorizationSignInResponseKind.Callback);
        Assert.NotNull(response.Callback);
        var code = response.Callback.Parameters[Parameters.Code];
        Assert.NotNull(code);
        return code;
    }

    private static async Task<string> ReadCodePayload(WebAppFactory factory, string code) {
        using var scope   = factory.Services.CreateScope();
        var       factory2 = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await factory2.CreateDbContextAsync();
        var token = await context.Tokens.SingleAsync(t => t.ReferenceId == code);
        Assert.NotNull(token.Payload);
        return token.Payload;
    }

    private static async Task<Schemata.Authorization.Skeleton.Models.AuthorizeRequest> ReadInteractionRequest(
        WebAppFactory factory,
        string        reference
    ) {
        using var scope     = factory.Services.CreateScope();
        var       dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await dbFactory.CreateDbContextAsync();
        var token = await context.Tokens.SingleAsync(t => t.ReferenceId == reference);
        Assert.Equal(TokenTypes.Interaction, token.Type);

        var json = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<JsonSerializerOptions>>();
        var request = JsonSerializer.Deserialize<Schemata.Authorization.Skeleton.Models.AuthorizeRequest>(token.Payload!, json.Value);
        Assert.NotNull(request);
        return request;
    }

    private static List<KeyValuePair<string, string>> ExchangeForm(string code, string? details) {
        var fields = new List<KeyValuePair<string, string>> {
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
        };
        if (details is not null) {
            fields.Add(new("authorization_details", details));
        }

        return fields;
    }

    private static List<KeyValuePair<string, string>> RefreshForm(string refresh, string? details) {
        var fields = new List<KeyValuePair<string, string>> {
            new("grant_type", GrantTypes.RefreshToken),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("refresh_token", refresh),
        };
        if (details is not null) {
            fields.Add(new("authorization_details", details));
        }

        return fields;
    }

    private static HttpRequestMessage Token(List<KeyValuePair<string, string>> fields) {
        return new(HttpMethod.Post, Endpoints.Token) { Content = new FormUrlEncodedContent(fields) };
    }

    /// <summary>Decodes a JWT payload segment into its raw JSON so the claim array stays visible.</summary>
    private static JsonElement Payload(string jwt) {
        using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private static void AssertDetails(JsonElement details, string[] actions, string[] institutions) {
        Assert.Equal(JsonValueKind.Array, details.ValueKind);
        var element = Assert.Single(details.EnumerateArray());
        Assert.Equal("payment_initiation", element.GetProperty("type").GetString());
        Assert.Equal(actions, element.GetProperty("actions").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(institutions, element.GetProperty("institutions").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    private static void AssertEmptyDetails(JsonElement details) {
        Assert.Equal(JsonValueKind.Array, details.ValueKind);
        Assert.Empty(details.EnumerateArray());
    }


    private static async Task AssertInvalidAuthorizationDetails(HttpResponseMessage response) {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidAuthorizationDetails, error.GetProperty("error").GetString());
    }
}