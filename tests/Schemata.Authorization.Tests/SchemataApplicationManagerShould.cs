using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class SchemataApplicationManagerShould
{
    private static SchemataApplicationManager<SchemataApplication, SchemataAuthorization> CreateManager() {
        return new(new Mock<System.IServiceProvider>().Object,
            new Mock<IResourceMutation<SchemataApplication>>().Object);
    }

    private static SchemataApplication AppWith(params string[] uris) {
        return new() { ClientId = "client-1", RedirectUris = [.. uris] };
    }

    [Theory]
    [InlineData("code id_token", "id_token code",  true)]
    [InlineData("code id_token", "code  id_token", true)]
    [InlineData("code  id_token", "id_token code", true)]
    [InlineData("code id_token", "code id_token",  true)]
    [InlineData("code id_token", "code",           false)]
    [InlineData("code id_token", "id_token token", false)]
    [InlineData("code id_token", "",               false)]
    [InlineData("code id_token", "   ",            false)]
    public async Task Admit_Response_Type_Permutations_Identically_Across_Helper_Manager_And_Mock(
        string registered, string query, bool expected) {
        var app           = AppWith("https://rp.example/cb");
        app.ResponseTypes = [registered, ResponseTypes.Token];
        var manager = CreateManager();
        var mock    = new Mock<IApplicationManager<SchemataApplication>>();
        mock.SetupTypedMetadata();

        var byHelper  = SchemataApplicationMetadata.HasResponseType(app, query);
        var byManager = await manager.HasResponseTypeAsync(app, query);
        var byMock    = await mock.Object.HasResponseTypeAsync(app, query);

        Assert.Equal(expected, byHelper);
        Assert.Equal(byHelper, byManager);
        Assert.Equal(byHelper, byMock);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Guard_Blank_Response_Type_Queries_The_Same_Way_In_Manager_And_Mock(string? query) {
        var app           = AppWith("https://rp.example/cb");
        app.ResponseTypes = [ResponseTypes.Code];
        var manager = CreateManager();
        var mock    = new Mock<IApplicationManager<SchemataApplication>>();
        mock.SetupTypedMetadata();

        Assert.False(await manager.HasResponseTypeAsync(app, query));
        Assert.False(await mock.Object.HasResponseTypeAsync(app, query));

        Assert.False(await manager.HasResponseTypeAsync(null, ResponseTypes.Code));
        Assert.False(await mock.Object.HasResponseTypeAsync(null, ResponseTypes.Code));
    }

    [Theory]
    // The RFC 8252 §7.3 exemption: only the port may differ on http loopback IP literals.
    [InlineData("http://127.0.0.1:4200/cb",          "http://127.0.0.1:9999/cb",          true)]
    [InlineData("http://127.0.0.1:4200/cb",          "http://127.0.0.1:4200/cb",          true)]
    [InlineData("http://[::1]:4200/cb",              "http://[::1]:1/cb",                 true)]
    [InlineData("http://127.0.0.1/cb",               "http://127.0.0.1:8080/cb",          true)]
    [InlineData("http://127.0.0.1:1/cb?a=1&b=2",     "http://127.0.0.1:2/cb?a=1&b=2",     true)]
    // Every other raw difference fails, including spellings Uri would normalize away.
    [InlineData("http://127.0.0.1:4200/cb",          "http://127.0.0.1:9999/other",       false)]
    [InlineData("http://127.0.0.1:1/cb?a=1",         "http://127.0.0.1:2/cb?a=2",         false)]
    [InlineData("http://127.0.0.1:1/cb?a=1&b=2",     "http://127.0.0.1:2/cb?b=2&a=1",     false)]
    [InlineData("HTTP://127.0.0.1:1/cb",             "http://127.0.0.1:2/cb",             false)]
    [InlineData("https://127.0.0.1:1/cb",            "https://127.0.0.1:2/cb",            false)]
    [InlineData("http://127.1:1/cb",                 "http://127.0.0.1:2/cb",             false)]
    [InlineData("http://127.1:1/cb",                 "http://127.1:2/cb",                 false)]
    [InlineData("http://127.0.0.1:1/cb",             "http://[::1]:1/cb",                 false)]
    [InlineData("http://127.0.0.1:1/cb/a%7eb",       "http://127.0.0.1:2/cb/a~b",         false)]
    [InlineData("http://127.0.0.1:1/a/../cb",        "http://127.0.0.1:2/cb",             false)]
    [InlineData("http://user@127.0.0.1:1/cb",        "http://127.0.0.1:2/cb",             false)]
    // localhost is outside the §7.3 exemption: it matches only through exact text.
    [InlineData("http://localhost:4200/cb",          "http://localhost:9999/cb",          false)]
    [InlineData("http://localhost:4200/cb",          "http://localhost:4200/cb",          true)]
    // Non-loopback profiles require exact ordinal text; no case, port, or query leniency.
    [InlineData("https://rp.example/cb",             "https://rp.example/cb",             true)]
    [InlineData("https://rp.example/cb",             "https://rp.example/cb?x=1",         false)]
    [InlineData("https://rp.example:443/cb",         "https://rp.example/cb",             false)]
    [InlineData("https://RP.example/cb",             "https://rp.example/cb",             false)]
    [InlineData("com.example.app:/cb",               "com.example.app:/cb",               true)]
    [InlineData("com.example.app:/cb",               "com.example.app:/cb?x=1",           false)]
    [InlineData("com.example.app:/cb",               "com.example.app:/other",            false)]
    public async Task Match_Loopback_Ip_Literals_With_Any_Port_Only(
        string registered, string requested, bool expected) {
        var manager = CreateManager();
        var app     = AppWith(registered);

        var result = await manager.ValidateRedirectUriAsync(app, requested);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("http://user@127.0.0.1:4200/cb")]
    [InlineData("http://127.0.0.1:4200/cb#frag")]
    [InlineData("https://user@rp.example/cb")]
    [InlineData("https://rp.example/cb#frag")]
    [InlineData("com.example.app://user@host/cb")]
    [InlineData("com.example.app:/cb#frag")]
    public async Task Reject_Userinfo_And_Fragments_Even_When_Registered_And_Requested_Are_Identical(string uri) {
        var manager = CreateManager();
        var app     = AppWith(uri);

        Assert.False(await manager.ValidateRedirectUriAsync(app, uri));
    }

    [Fact]
    public async Task Persist_Full_Dcr_Client_Metadata_Round_Trip() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<ApplicationDbContext>(options => options
                     .UseSqlite(connection)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataApplication, EfCoreRepository<ApplicationDbContext, SchemataApplication>>();

        await using var root = services.BuildServiceProvider();

        await using (var db = root.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext()) {
            await db.Database.EnsureCreatedAsync();
        }

        var app = new SchemataApplication {
            ClientId                  = "dcr-full",
            Name                      = "client-resource",
            RedirectUris              = ["https://rp.example/cb"],
            Contacts                  = ["admin@rp.example", "tech@rp.example"],
            LogoUri                   = "https://rp.example/logo.png",
            ClientUri                 = "https://rp.example",
            PolicyUri                 = "https://rp.example/privacy",
            TosUri                    = "https://rp.example/terms",
            GrantTypes                = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken],
            ResponseTypes             = [ResponseTypes.Code],
            Scope                     = "openid profile",
            TokenEndpointAuthMethod   = ClientAuthMethods.ClientSecretPost,
            RequireAuthTime           = true,
            DefaultMaxAge             = "3600",
            DefaultAcrValues          = ["urn:example:acr:silver"],
            InitiateLoginUri          = "https://rp.example/login",
            SoftwareId                = "4NRB1-0XZGZ-Y09VD-2J8BX",
            SoftwareVersion           = "1.2.3",
            SoftwareStatement         = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJodHRwczovL3NvZnR3YXJlLmV4YW1wbGUifQ.sig",
            AuthorizationDetailsTypes = ["payment_initiation"],
            RequirePushedAuthorizationRequests = true,
            RequestObjectSigningAlg = SigningAlgorithms.RsaSha256,
            RequireSignedRequestObject  = true,
            DpopBoundAccessTokens       = true,
        };

        await using (var scope = root.CreateAsyncScope()) {
            var manager = new SchemataApplicationManager<SchemataApplication, SchemataAuthorization>(
                scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataApplication>>());

            await manager.CreateAsync(app);
        }

        SchemataApplication? loaded;
        await using (var scope = root.CreateAsyncScope()) {
            var manager = new SchemataApplicationManager<SchemataApplication, SchemataAuthorization>(
                scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataApplication>>());

            loaded = await manager.FindByClientIdAsync("dcr-full");
        }

        Assert.NotNull(loaded);
        Assert.NotSame(app, loaded);
        Assert.Equal("client-resource", loaded.Name);
        Assert.Equal("dcr-full", loaded.ClientId);
        Assert.Equal(app.RedirectUris, loaded.RedirectUris);
        Assert.Equal(app.Contacts, loaded.Contacts);
        Assert.Equal(app.LogoUri, loaded.LogoUri);
        Assert.Equal(app.ClientUri, loaded.ClientUri);
        Assert.Equal(app.PolicyUri, loaded.PolicyUri);
        Assert.Equal(app.TosUri, loaded.TosUri);
        Assert.Equal(app.GrantTypes, loaded.GrantTypes);
        Assert.Equal(app.ResponseTypes, loaded.ResponseTypes);
        Assert.Equal("openid profile", loaded.Scope);
        Assert.Equal(ClientAuthMethods.ClientSecretPost, loaded.TokenEndpointAuthMethod);
        Assert.Equal(app.RequireAuthTime, loaded.RequireAuthTime);
        Assert.Equal(app.DefaultMaxAge, loaded.DefaultMaxAge);
        Assert.Equal(app.DefaultAcrValues, loaded.DefaultAcrValues);
        Assert.Equal(app.InitiateLoginUri, loaded.InitiateLoginUri);
        Assert.Equal(app.SoftwareId, loaded.SoftwareId);
        Assert.Equal(app.SoftwareVersion, loaded.SoftwareVersion);
        Assert.Equal(app.SoftwareStatement, loaded.SoftwareStatement);
        Assert.Equal(app.AuthorizationDetailsTypes, loaded.AuthorizationDetailsTypes);
        Assert.Equal(app.RequirePushedAuthorizationRequests, loaded.RequirePushedAuthorizationRequests);
        Assert.Equal(app.RequestObjectSigningAlg, loaded.RequestObjectSigningAlg);
        Assert.Equal(app.RequireSignedRequestObject, loaded.RequireSignedRequestObject);
        Assert.True(loaded.DpopBoundAccessTokens);

        // Admission runs against the reloaded entity, so the persisted typed fields are the
        // ones the runtime enforces.
        var read = new SchemataApplicationManager<SchemataApplication, SchemataAuthorization>(
            new Mock<System.IServiceProvider>().Object,
            new Mock<IResourceMutation<SchemataApplication>>().Object);
        Assert.True(await read.HasGrantTypeAsync(loaded, GrantTypes.RefreshToken));
        Assert.False(await read.HasGrantTypeAsync(loaded, GrantTypes.ClientCredentials));
        Assert.True(await read.HasResponseTypeAsync(loaded, ResponseTypes.Code));
        Assert.False(await read.HasResponseTypeAsync(loaded, ResponseTypes.Token));
        Assert.True(await read.HasScopeAsync(loaded, Scopes.Profile));
        Assert.False(await read.HasScopeAsync(loaded, "admin"));
    }

    private sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataApplication> Applications { get; set; } = null!;
    }
}