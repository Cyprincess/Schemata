using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Authorization.Tests;

public class SchemataApplicationManagerShould
{
    private static SchemataApplicationManager<SchemataApplication> CreateManager() {
        return new(new Mock<IRepository<SchemataApplication>>().Object);
    }

    private static SchemataApplication AppWith(params string[] uris) {
        return new() { ClientId = "client-1", RedirectUris = [.. uris] };
    }

    [Theory]
    [InlineData("http://127.0.0.1:4200/cb", "http://127.0.0.1:9999/cb", true)]
    [InlineData("http://127.0.0.1:4200/cb", "http://127.0.0.1:4200/cb", true)]
    [InlineData("http://127.0.0.1:4200/cb", "http://127.0.0.1:9999/other", false)]
    [InlineData("http://[::1]:4200/cb",     "http://[::1]:1/cb",          true)]
    [InlineData("http://localhost:4200/cb", "http://localhost:9999/cb",  false)]
    [InlineData("https://rp.example/cb",    "https://rp.example/cb",     true)]
    [InlineData("https://rp.example/cb",    "https://rp.example/cb?x=1",  false)]
    public async Task Match_Loopback_Ip_Literals_With_Any_Port_Only(
        string registered, string requested, bool expected) {
        var manager = CreateManager();
        var app     = AppWith(registered);

        var result = await manager.ValidateRedirectUriAsync(app, requested);

        Assert.Equal(expected, result);
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
            RedirectUris              = ["https://rp.example/cb"],
            Contacts                  = ["admin@rp.example", "tech@rp.example"],
            LogoUri                   = "https://rp.example/logo.png",
            ClientUri                 = "https://rp.example",
            PolicyUri                 = "https://rp.example/privacy",
            TosUri                    = "https://rp.example/terms",
            RequireAuthTime           = true,
            DefaultMaxAge             = "3600",
            DefaultAcrValues          = ["urn:example:acr:silver"],
            InitiateLoginUri          = "https://rp.example/login",
            SoftwareId                = "4NRB1-0XZGZ-Y09VD-2J8BX",
            SoftwareVersion           = "1.2.3",
            SoftwareStatement         = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJodHRwczovL3NvZnR3YXJlLmV4YW1wbGUifQ.sig",
            AuthorizationDetailsTypes = ["payment_initiation"],
        };

        await using (var scope = root.CreateAsyncScope()) {
            var manager = new SchemataApplicationManager<SchemataApplication>(
                scope.ServiceProvider.GetRequiredService<IRepository<SchemataApplication>>());

            await manager.CreateAsync(app);
        }

        SchemataApplication? loaded;
        await using (var scope = root.CreateAsyncScope()) {
            var manager = new SchemataApplicationManager<SchemataApplication>(
                scope.ServiceProvider.GetRequiredService<IRepository<SchemataApplication>>());

            loaded = await manager.FindByClientIdAsync("dcr-full");
        }

        Assert.NotNull(loaded);
        Assert.NotSame(app, loaded);
        Assert.Equal(app.RedirectUris, loaded.RedirectUris);
        Assert.Equal(app.Contacts, loaded.Contacts);
        Assert.Equal(app.LogoUri, loaded.LogoUri);
        Assert.Equal(app.ClientUri, loaded.ClientUri);
        Assert.Equal(app.PolicyUri, loaded.PolicyUri);
        Assert.Equal(app.TosUri, loaded.TosUri);
        Assert.Equal(app.RequireAuthTime, loaded.RequireAuthTime);
        Assert.Equal(app.DefaultMaxAge, loaded.DefaultMaxAge);
        Assert.Equal(app.DefaultAcrValues, loaded.DefaultAcrValues);
        Assert.Equal(app.InitiateLoginUri, loaded.InitiateLoginUri);
        Assert.Equal(app.SoftwareId, loaded.SoftwareId);
        Assert.Equal(app.SoftwareVersion, loaded.SoftwareVersion);
        Assert.Equal(app.SoftwareStatement, loaded.SoftwareStatement);
        Assert.Equal(app.AuthorizationDetailsTypes, loaded.AuthorizationDetailsTypes);
    }

    private sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataApplication> Applications { get; set; } = null!;
    }
}