using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Core.Json;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class RegistrationMetadataShould
{
    private static readonly IHttpClientFactory Http = new StubFactory();

    private static readonly IOptions<SchemataAuthorizationOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions());

    private static RegisterRequest Request(
        string[]?  redirectUris = null,
        string?    applicationType = null,
        string[]?  postLogout = null,
        string?    frontChannel = null,
        string?    backChannel = null
    ) {
        return new() {
            RedirectUris           = [.. (redirectUris ?? ["https://rp.example/cb"])],
            ApplicationType        = applicationType,
            PostLogoutRedirectUris = postLogout is null ? null : [.. postLogout],
            FrontChannelLogoutUri  = frontChannel,
            BackChannelLogoutUri   = backChannel,
        };
    }

    [Fact]
    public async Task Store_Standard_Metadata_In_Typed_Fields_Without_Permission_Entries() {
        var request = Request();
        request.GrantTypes = ["authorization_code", "refresh_token"];
        request.ResponseTypes = ["code"];
        request.Scope = "openid profile";

        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);

        Assert.Equal(["authorization_code", "refresh_token"], app.GrantTypes);
        Assert.Equal(["code"], app.ResponseTypes);
        Assert.Equal("openid profile", app.Scope);
        // Protocol metadata is never mirrored into the permission vocabulary.
        Assert.Null(app.Permissions);
    }

    [Fact]
    public async Task Preserve_And_Replace_Language_Tagged_Metadata_Without_Unknown_Policy() {
        var request = JsonSerializer.Deserialize<RegisterRequest>("""
            {"redirect_uris":["https://rp.example/cb"],"client_name#fr-CA":"Nom","logo_uri#fr-CA":"https://rp.example/fr.png","permissions":["e:token"]}
            """, Ambient)!;
        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);
        Assert.Equal("Nom", app.DisplayNames!["fr-CA"]);
        Assert.Null(app.Permissions);
        var response = await RegistrationMetadataMapper.ToResponse(app, new TestSecurityStore());
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(response, Ambient));
        Assert.Equal("Nom", wire.RootElement.GetProperty("client_name#fr-CA").GetString());
        Assert.Equal("https://rp.example/fr.png", wire.RootElement.GetProperty("logo_uri#fr-CA").GetString());
        Assert.False(wire.RootElement.TryGetProperty("permissions", out _));
        await RegistrationMetadataMapper.ApplyAsync(app, Request(), Options, Http);
        response = await RegistrationMetadataMapper.ToResponse(app, new TestSecurityStore());
        Assert.Null(response.LocalizedMetadata);
    }

    [Fact]
    public async Task Classify_A_Native_Client_With_A_Static_Secret_As_Public() {
        // RFC 8252 §8: a secret embedded in a distributed native application is public knowledge
        // and never confers confidentiality, regardless of the declared auth method.
        var request = Request(applicationType: ApplicationTypes.Native);
        request.TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost;

        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);

        Assert.False(app.IsConfidential);
    }

    [Fact]
    public async Task Classify_A_Web_Client_With_A_Credential_Method_As_Confidential() {
        var request = Request();
        request.TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost;

        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);

        Assert.True(app.IsConfidential);
    }

    [Theory]
    [InlineData("https://app.example/cb")]   // claimed https app link
    [InlineData("com.example.app:/cb")]      // private-use URI scheme
    [InlineData("http://127.0.0.1:8323/cb")] // loopback IP literal
    public async Task Accept_The_Three_Rfc8252_Native_Redirect_Profiles(string uri) {
        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
            Request([uri], applicationType: ApplicationTypes.Native), Options, Http);

        Assert.Contains(uri, app.RedirectUris!);
    }

    [Theory]
    [InlineData("https://rp.example/cb#fragment")]
    [InlineData("com.example.app:/cb#fragment")]
    public async Task Reject_Redirect_Uris_With_Fragments_On_Any_Profile(string uri) {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
                Request([uri], applicationType: ApplicationTypes.Native), Options, Http));

        Assert.Equal(OAuthErrors.InvalidRedirectUri, ex.Status);
    }

    [Theory]
    [InlineData("https://user@rp.example/cb",          ApplicationTypes.Web)]
    [InlineData("https://user@app.example/cb",         ApplicationTypes.Native)]
    [InlineData("http://user@127.0.0.1:8323/cb",       ApplicationTypes.Native)]
    [InlineData("com.example.app://user@host/cb",      ApplicationTypes.Native)]
    public async Task Reject_Redirect_Uris_With_Userinfo_On_Any_Profile(string uri, string applicationType) {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
                Request([uri], applicationType: applicationType), Options, Http));

        Assert.Equal(OAuthErrors.InvalidRedirectUri, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Abbreviated_Loopback_Ip_Literal() {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
                Request(["http://127.1:8323/cb"], applicationType: ApplicationTypes.Native), Options, Http));

        Assert.Equal(OAuthErrors.InvalidRedirectUri, ex.Status);
    }

    [Fact]
    public async Task Reject_A_Post_Logout_Uri_With_A_Fragment() {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
                Request(postLogout: ["https://rp.example/after#x"]), Options, Http));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Theory]
    [InlineData("http://rp.example/logout")]
    [InlineData("https://rp.example/logout#f")]
    public async Task Require_Absolute_Https_For_The_Back_Channel_Logout_Uri(string uri) {
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
                Request(backChannel: uri), Options, Http));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Fact]
    public async Task Require_Private_Scheme_Domain_Unless_Explicitly_Relaxed() {
        var request = Request(["myapp:/cb"], ApplicationTypes.Native);
        var error = await Assert.ThrowsAsync<OAuthException>(() => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http));
        Assert.Equal(OAuthErrors.InvalidRedirectUri, error.Status);
        var relaxed = Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions { RequireNativeSchemeDomain = false });
        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, relaxed, Http);
        Assert.Contains("myapp:/cb", app.RedirectUris!);
    }

    [Theory]
    [InlineData("https://other.example/logout")]
    [InlineData("https://rp.example:444/logout")]
    public async Task Require_Front_Channel_Origin_To_Match_A_Redirect(string logout) {
        var error = await Assert.ThrowsAsync<OAuthException>(() => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(Request(frontChannel: logout), Options, Http));
        Assert.Equal(OAuthErrors.InvalidClientMetadata, error.Status);
        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(Request(backChannel: logout), Options, Http);
        Assert.Equal(logout, app.BackChannelLogoutUri);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Require_Http_Logout_Policy_And_Confidential_Client(bool allowed, bool confidential) {
        var request = Request(postLogout: ["http://rp.example/after"]);
        request.TokenEndpointAuthMethod = confidential ? ClientAuthMethods.ClientSecretBasic : ClientAuthMethods.None;
        var configured = Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions { AllowHttpLogoutUris = allowed });
        if (allowed && confidential) {
            var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, configured, Http);
            Assert.Contains("http://rp.example/after", app.PostLogoutRedirectUris!);
        } else {
            var error = await Assert.ThrowsAsync<OAuthException>(() => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, configured, Http));
            Assert.Equal(OAuthErrors.InvalidClientMetadata, error.Status);
        }
    }

    [Fact]
    public async Task Preserve_Native_Post_Logout_Callback() {
        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
            Request(["com.example.app:/cb"], ApplicationTypes.Native, ["com.example.app:/logout"]), Options, Http);
        Assert.Contains("com.example.app:/logout", app.PostLogoutRedirectUris!);
    }

    private static readonly DateTime RowTime = new(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);

    private const string Jwks = """{"keys":[{"kty":"RSA","kid":"rp-2","use":"sig","n":"y","e":"AQAB"}]}""";

    private static readonly JsonSerializerOptions Ambient = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters           = { JsonStringNumberConverter.Instance },
    };

    private static SchemataApplication StoredApp() {
        return new() {
            ClientId      = "rp-1",
            Name          = "rp-1",
            CanonicalName = "applications/rp-1",
            RedirectUris  = ["https://rp.example/cb"],
        };
    }

    private static SchemataSecurity KeyRow(
        string    kind,
        string?   value,
        string    status  = SecurityConstants.Statuses.Valid,
        DateTime? created = null
    ) {
        return new() {
            Parent     = "applications/rp-1",
            Key        = $"key-{Guid.NewGuid():n}",
            Kind       = kind,
            Usage      = SecurityConstants.Usages.Authentication,
            Value      = value,
            Status     = status,
            CreateTime = created ?? RowTime,
        };
    }

    [Theory]
    [InlineData("""[{"kty":"RSA"}]""")]
    [InlineData("\"a string\"")]
    [InlineData("42")]
    [InlineData("true")]
    public async Task Reject_A_Non_Object_Jwks_Value(string jwks) {
        var request    = Request();
        request.Jwks   = JsonDocument.Parse(jwks).RootElement.Clone();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Fact]
    public async Task Map_The_Effective_Protocol_Fields_In_Invariant_Form() {
        var request = Request();
        request.DefaultMaxAge                          = 900;
        request.TokenEndpointAuthMethod                = ClientAuthMethods.ClientSecretPost;
        request.SoftwareStatement                      = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJodHRwczovL3NvZnR3YXJlLmV4YW1wbGUifQ.sig";
        request.Scope                                  = "  openid   profile  ";

        // The stored max age keeps invariant digits; a culture with non-Latin numerals would
        // surface a CurrentCulture regression here.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new("fa-IR");
        SchemataApplication app;
        try {
            app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);
        } finally {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal("900", app.DefaultMaxAge);
        Assert.Equal(ClientAuthMethods.ClientSecretPost, app.TokenEndpointAuthMethod);
        Assert.Equal(request.SoftwareStatement, app.SoftwareStatement);
        Assert.Equal("openid profile", app.Scope);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData((long)int.MaxValue + 1)]
    public async Task Reject_A_Default_Max_Age_Outside_The_Runtime_Range(long maxAge) {
        var request = Request();
        request.DefaultMaxAge = maxAge;

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData((long)int.MaxValue)]
    public async Task Map_And_Read_Back_A_Default_Max_Age_Inside_The_Runtime_Range(long maxAge) {
        var request = Request();
        request.DefaultMaxAge = maxAge;

        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);

        Assert.Equal(maxAge.ToString(CultureInfo.InvariantCulture), app.DefaultMaxAge);

        var response = await RegistrationMetadataMapper.ToResponse(app, new TestSecurityStore());

        Assert.Equal(maxAge, response.DefaultMaxAge);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Default_The_Auth_Method_To_Client_Secret_Basic_When_Omitted_Or_Blank(string? method) {
        var request                 = Request();
        request.TokenEndpointAuthMethod = method;

        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);

        Assert.Equal(ClientAuthMethods.ClientSecretBasic, app.TokenEndpointAuthMethod);
    }

    [Fact]
    public async Task Reject_An_Auth_Method_Outside_The_Server_Allowed_Set() {
        var request                 = Request();
        request.TokenEndpointAuthMethod = ClientAuthMethods.PrivateKeyJwt;

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, ex.Status);
    }

    [Fact]
    public async Task Echo_The_Valid_Jwks_Row_And_Ignore_A_Newer_Revoked_One() {
        var store = new TestSecurityStore();
        await store.CreateAsync(KeyRow(SecurityConstants.Kinds.Jwks, Jwks, SecurityConstants.Statuses.Valid, RowTime));
        await store.CreateAsync(
            KeyRow(SecurityConstants.Kinds.Jwks, """{"keys":[{"kty":"RSA","kid":"rp-3"}]}""",
                SecurityConstants.Statuses.Revoked, RowTime.AddDays(1)));

        var response = await RegistrationMetadataMapper.ToResponse(StoredApp(), store);

        Assert.Equal(Jwks, response.Jwks!.Value.GetRawText());
    }

    [Fact]
    public async Task Return_Only_The_Current_Key_Kind_On_Read_Back() {
        var store = new TestSecurityStore();
        await store.CreateAsync(
            KeyRow(SecurityConstants.Kinds.Jwks, Jwks, SecurityConstants.Statuses.Revoked, RowTime.AddDays(1)));
        await store.CreateAsync(
            KeyRow(SecurityConstants.Kinds.JwksUri, "https://rp.example/jwks.json", SecurityConstants.Statuses.Valid, RowTime));

        var response = await RegistrationMetadataMapper.ToResponse(StoredApp(), store);

        Assert.Null(response.Jwks);
        Assert.Equal("https://rp.example/jwks.json", response.JwksUri);
    }

    [Theory]
    [InlineData("{\"keys\":[{\"kty\":\"oct\",\"k\":\"secret\"}]}")]
    [InlineData("{\"keys\":[{\"kty\":\"RSA\",\"d\":\"private\"}]}")]
    [InlineData("{\"keys\":[{\"kty\":\"EC\",\"d\":\"private\"}]}")]
    public async Task Reject_Private_Or_Symmetric_Public_Key_Metadata(string json) {
        var request = Request();
        request.Jwks = JsonSerializer.Deserialize<JsonElement>(json);
        var error = await Assert.ThrowsAsync<OAuthException>(() => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http));
        Assert.Equal(OAuthErrors.InvalidClientMetadata, error.Status);
        var store = new TestSecurityStore();
        await store.CreateAsync(KeyRow(SecurityConstants.Kinds.Jwks, json));
        error = await Assert.ThrowsAsync<OAuthException>(() => RegistrationMetadataMapper.ToResponse(StoredApp(), store));
        Assert.Equal(OAuthErrors.InvalidClientMetadata, error.Status);
    }

    [Fact]
    public async Task Omit_Both_Key_Kinds_When_Every_Row_Is_Revoked() {
        var store = new TestSecurityStore();
        await store.CreateAsync(KeyRow(SecurityConstants.Kinds.Jwks, Jwks, SecurityConstants.Statuses.Revoked, RowTime));
        await store.CreateAsync(
            KeyRow(SecurityConstants.Kinds.JwksUri, "https://rp.example/jwks.json", SecurityConstants.Statuses.Revoked, RowTime));

        var response = await RegistrationMetadataMapper.ToResponse(StoredApp(), store);

        Assert.Null(response.Jwks);
        Assert.Null(response.JwksUri);
    }

    [Theory]
    [InlineData("900", 900L)]
    [InlineData("abc", null)]
    [InlineData(" 900", null)]
    [InlineData("-1", null)]
    public async Task Parse_The_Stored_Default_Max_Age_As_Invariant_Digits_Only(string stored, long? expected) {
        var app          = StoredApp();
        app.DefaultMaxAge = stored;

        var response = await RegistrationMetadataMapper.ToResponse(app, new TestSecurityStore());

        Assert.Equal(expected, response.DefaultMaxAge);
    }

    [Fact]
    public async Task Echo_The_Effective_Fields_As_Wire_Shapes_Under_The_Ambient_Options() {
        var request = Request();
        request.DefaultMaxAge                          = 900;
        request.TokenEndpointAuthMethod                = ClientAuthMethods.ClientSecretPost;
        request.SoftwareStatement                      = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJodHRwczovL3NvZnR3YXJlLmV4YW1wbGUifQ.sig";
        var store = new TestSecurityStore();
        await store.CreateAsync(KeyRow(SecurityConstants.Kinds.Jwks, Jwks, SecurityConstants.Statuses.Valid, RowTime));

        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);
        app.Name          = "rp-1";
        app.CanonicalName = "applications/rp-1";

        var response = await RegistrationMetadataMapper.ToResponse(app, store);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, Ambient));
        var root = document.RootElement;

        var maxAge = root.GetProperty("default_max_age");
        Assert.Equal(JsonValueKind.Number, maxAge.ValueKind);
        Assert.Equal(900, maxAge.GetInt64());
        Assert.Equal(Jwks, root.GetProperty("jwks").GetRawText());
        Assert.Equal(ClientAuthMethods.ClientSecretPost, root.GetProperty("token_endpoint_auth_method").GetString());
        Assert.Equal(request.SoftwareStatement, root.GetProperty("software_statement").GetString());
    }

    [Fact]
    public async Task Store_An_OAuth_Only_Profile_Without_Redirect_Or_Code_Defaults() {
        var request = new RegisterRequest {
            GrantTypes = [GrantTypes.ClientCredentials],
        };

        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);

        Assert.Null(app.RedirectUris);
        Assert.Equal([GrantTypes.ClientCredentials], app.GrantTypes);
        Assert.Null(app.ResponseTypes);
    }

    [Fact]
    public async Task Keep_Code_Defaults_And_Redirect_Validation_For_An_Oidc_Profile() {
        var request = Request();
        request.Scope = "openid";

        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);

        Assert.Equal([GrantTypes.AuthorizationCode], app.GrantTypes);
        Assert.Equal([ResponseTypes.Code], app.ResponseTypes);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
                new() { Scope = "openid" }, Options, Http));
        Assert.Equal(OAuthErrors.InvalidRedirectUri, ex.Status);
    }

    [Fact]
    public async Task Clear_Redirect_Metadata_When_A_Replace_Goes_OAuth_Only() {
        var app = StoredApp();
        app.GrantTypes    = [GrantTypes.AuthorizationCode];
        app.ResponseTypes = [ResponseTypes.Code];
        app.Scope         = "openid";

        await RegistrationMetadataMapper.ApplyAsync(app, new() {
            GrantTypes = [GrantTypes.ClientCredentials],
            Scope      = "api",
        }, Options, Http);

        Assert.Null(app.RedirectUris);
        Assert.Null(app.ResponseTypes);
        Assert.Equal([GrantTypes.ClientCredentials], app.GrantTypes);
        Assert.Equal("api", app.Scope);
    }

    [Theory]
    [MemberData(nameof(OidcOnlySignals))]
    public async Task Oidc_Only_Metadata_Selects_The_Redirect_Profile(string signal, Action<RegisterRequest> apply) {
        var request = new RegisterRequest();
        apply(request);

        // The signal alone selects the interactive profile: redirect_uris cannot be omitted.
        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http));
        Assert.True(OAuthErrors.InvalidRedirectUri == ex.Status, $"signal {signal}: expected invalid_redirect_uri, got {ex.Status}");

        // With redirect URIs registered, the same signal keeps the OIDC code defaults.
        request.RedirectUris = ["https://rp.example/cb"];
        var app = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(request, Options, Http);

        Assert.Equal([GrantTypes.AuthorizationCode], app.GrantTypes);
        Assert.Equal([ResponseTypes.Code], app.ResponseTypes);
    }

    public static IEnumerable<object[]> OidcOnlySignals() {
        yield return ["default_max_age", (Action<RegisterRequest>)(r => r.DefaultMaxAge = 900)];
        yield return ["require_auth_time", (Action<RegisterRequest>)(r => r.RequireAuthTime = true)];
        yield return ["default_acr_values", (Action<RegisterRequest>)(r => r.DefaultAcrValues = ["urn:example:acr:1"])];
        yield return ["initiate_login_uri", (Action<RegisterRequest>)(r => r.InitiateLoginUri = "https://rp.example/login")];
        yield return ["post_logout_redirect_uris", (Action<RegisterRequest>)(r => r.PostLogoutRedirectUris = ["https://rp.example/after"])];
        yield return ["frontchannel_logout_uri", (Action<RegisterRequest>)(r => r.FrontChannelLogoutUri = "https://rp.example/front")];
        yield return ["frontchannel_logout_session_required", (Action<RegisterRequest>)(r => r.FrontChannelLogoutSessionRequired = true)];
        yield return ["backchannel_logout_uri", (Action<RegisterRequest>)(r => r.BackChannelLogoutUri = "https://rp.example/back")];
        yield return ["backchannel_logout_session_required", (Action<RegisterRequest>)(r => r.BackChannelLogoutSessionRequired = true)];
        yield return ["userinfo_encrypted_response_enc", (Action<RegisterRequest>)(r => r.UserinfoEncryptedResponseEnc = ContentEncryptionAlgorithms.Aes128CbcHmacSha256)];
    }

    [Fact]
    public async Task Replace_With_Oidc_Only_Metadata_Still_Requires_Redirect_Uris() {
        var app = StoredApp();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => RegistrationMetadataMapper.ApplyAsync(app, new() {
                RequireAuthTime = true,
            }, Options, Http));

        Assert.Equal(OAuthErrors.InvalidRedirectUri, ex.Status);
        // Validation precedes assignment: a failed replace leaves the stored values untouched.
        Assert.Equal(["https://rp.example/cb"], app.RedirectUris);
    }

    [Fact]
    public async Task Replace_With_Oidc_Only_Metadata_Keeps_The_Code_Defaults() {
        var app = StoredApp();
        app.GrantTypes = [GrantTypes.ClientCredentials];

        await RegistrationMetadataMapper.ApplyAsync(app, new() {
            RedirectUris    = ["https://rp.example/cb"],
            RequireAuthTime = true,
        }, Options, Http);

        Assert.Equal([GrantTypes.AuthorizationCode], app.GrantTypes);
        Assert.Equal([ResponseTypes.Code], app.ResponseTypes);
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) {
            return new(new StubHandler());
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new StringContent("[]"),
            });
        }
    }
}
