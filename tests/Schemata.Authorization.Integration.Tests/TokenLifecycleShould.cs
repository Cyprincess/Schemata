using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public class TokenLifecycleShould
{
    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData(false, TokenStatuses.Redeemed)]
    [InlineData(true, TokenStatuses.Redeemed)]
    [InlineData(false, TokenStatuses.Revoked)]
    [InlineData(true, TokenStatuses.Revoked)]
    public async Task Fresh_Terminal_Decision_Cannot_Be_Redeemed_Or_Mutate_Its_Stamp(bool linq, string status) {
        await using var host = await Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        await store.CreateAsync(new() { Name = "decision", ReferenceId = "decision", Status = status, Type = TokenTypes.Logout });
        var current = (await store.FindByReferenceIdAsync("decision"))!;
        var stamp = current.Timestamp;
        Assert.False(await store.TryRedeemAsync(current));
        var unchanged = (await store.FindByReferenceIdAsync("decision"))!;
        Assert.Equal(status, unchanged.Status);
        Assert.Equal(stamp, unchanged.Timestamp);
        await store.CreateAsync(new() { Name = "next", ReferenceId = "next", Status = TokenStatuses.Valid });
        Assert.True(await store.TryRedeemAsync((await store.FindByReferenceIdAsync("next"))!));
    }

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_Scope_And_Consent_Managers_Observe_Detached_Updates(bool linq) {
        await using var host = await Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var scopes = new SchemataScopeManager<SchemataScope>(services, services.GetRequiredService<IResourceMutation<SchemataScope>>());
        await scopes.CreateAsync(new() { Name = "coherent", DisplayName = "before" });
        var oldScope = (await scopes.FindByNameAsync("coherent"))!;
        SchemataScope detached;
        await using (var repository = services.GetRequiredService<IRepository<SchemataScope>>())
            detached = (await repository.SingleOrDefaultAsync(q => q.Where(row => row.Name == "coherent")))!;
        detached.DisplayName = "after";
        await scopes.UpdateAsync(detached);
        Assert.Equal("after", (await scopes.FindByNameAsync("coherent"))!.DisplayName);
        Assert.Equal(detached.Timestamp, Assert.Single(await scopes.ListAsync().ToListAsync()).Timestamp);
        Assert.NotEqual(oldScope.Timestamp, detached.Timestamp);
        var applications = services.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var application = (await applications.CreateAsync(new() { Name = "consent-client", ClientId = "consent-client" }))!;
        var manager = new SchemataAuthorizationManager<SchemataAuthorization, SchemataApplication>(applications,
            services.GetRequiredService<IResourceMutation<SchemataAuthorization>>(), services);
        var grant = (await manager.CreateAsync(new() { Name = "consent", Subject = "users/coherent", Application = application.CanonicalName,
            Status = TokenStatuses.Valid }))!;
        var oldGrant = Assert.Single(await manager.ListAsync(grant.Subject, grant.Application).ToListAsync());
        SchemataAuthorization current;
        await using (var repository = services.GetRequiredService<IRepository<SchemataAuthorization>>())
            current = (await repository.SingleOrDefaultAsync(q => q.Where(row => row.Uid == grant.Uid)))!;
        await manager.RevokeAsync(current);
        var updated = Assert.Single(await manager.ListAsync(grant.Subject, grant.Application).ToListAsync());
        Assert.Equal(TokenStatuses.Revoked, updated.Status);
        Assert.Equal(current.Timestamp, updated.Timestamp);
        Assert.NotEqual(oldGrant.Timestamp, updated.Timestamp);
    }

    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_Manager_Reads_Detached_Update_Metadata_And_Durable_Stamp(bool linq) {
        await using var host = await Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        await manager.CreateAsync(new() { Name = "coherent", ClientId = "coherent", ClientName = "before" });
        var original = await manager.FindByClientIdAsync("coherent");
        Assert.NotNull(original);
        SchemataApplication current;
        using (var reader = host.Services.CreateScope()) {
            current = (await reader.ServiceProvider.GetRequiredService<IRepository<SchemataApplication>>()
                .SingleOrDefaultAsync(q => q.Where(a => a.ClientId == "coherent")))!;
        }
        current.ClientName = "after";
        await manager.UpdateAsync(current);
        var found = await manager.FindByClientIdAsync("coherent");
        var listed = Assert.Single(await manager.ListAsync(q => q.Where(a => a.ClientId == "coherent")).ToListAsync());
        Assert.Equal("after", found!.ClientName);
        Assert.Equal("after", listed.ClientName);
        Assert.Equal(current.Timestamp, found.Timestamp);
        Assert.Equal(current.Timestamp, listed.Timestamp);
        Assert.NotEqual(original.Timestamp, found.Timestamp);
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Replay_Revokes_Family_And_Grant_And_Allows_The_Next_Operation(bool linq, bool race) {
        await using var host = await Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<RepositoryTokenStore>();
        var application = new SchemataApplication {
            Name = "client", ClientId = "client", TokenEndpointAuthMethod = ClientAuthMethods.None,
        };
        var applications = services.GetRequiredService<IApplicationManager<SchemataApplication>>();
        await applications.CreateAsync(application);
        var grant = new SchemataAuthorization { Name = "grant", Application = application.CanonicalName, Status = TokenStatuses.Valid };
        await services.GetRequiredService<IResourceMutation<SchemataAuthorization>>().CreateAsync(grant);
        var refresh = new SchemataToken {
            Name = "refresh", ReferenceId = "refresh", Type = TokenTypes.RefreshToken,
            Status = TokenStatuses.Valid, Family = "family", Authorization = grant.CanonicalName,
        };
        Assert.True(await store.PublishFamilyAsync("family", [refresh], true,
            beforeCommit: (transaction, ct) => applications.EnlistTokenPublicationAsync(transaction, [refresh], ct)));
        var access = new SchemataToken {
            Name = "access", ReferenceId = "access", Type = TokenTypes.AccessToken,
            Status = TokenStatuses.Valid, Authorization = grant.CanonicalName,
        };
        await store.CreateAsync(access);
        var code = new SchemataToken {
            Name = "code", ReferenceId = "code", Type = TokenTypes.AuthorizationCode,
            Status = race ? TokenStatuses.Valid : TokenStatuses.Redeemed,
            Parent = "users/user", Application = application.CanonicalName,
            Family = "family", Authorization = grant.CanonicalName,
            Payload = JsonSerializer.Serialize(new {
                Request = new AuthorizeRequest { ClientId = "client", RedirectUri = "https://client.example/cb" },
                Grant = new AuthorizationGrantContext { Subject = "users/user", SubjectKind = GrantSubjectKinds.EndUser },
            }),
        };
        await store.CreateAsync(code);
        await using var appRepository = services.GetRequiredService<IRepository<SchemataApplication>>();
        var stamp = (await appRepository.SingleOrDefaultAsync(q => q.Where(a => a.Name == "client")))!.Timestamp;
        if (race) {
            services.GetRequiredService<RaceExchange>().Run = async () => {
                using var winner = host.Services.CreateScope();
                var tokens = winner.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
                Assert.True(await tokens.TryRedeemAsync((await tokens.FindByReferenceIdAsync("code"))!));
            };
        }
        using var ambient = AdviceContext.Establish(new(services));
        var handler = services.GetRequiredService<AuthorizationCodeHandler<SchemataApplication>>();
        var error = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(new TokenRequest {
            Code = "code", ClientId = "client", RedirectUri = "https://client.example/cb",
        }, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidGrant, error.Status);
        Assert.False(await store.IsFamilyActiveAsync("family"));
        Assert.Equal(TokenStatuses.Revoked, (await store.FindByReferenceIdAsync("refresh"))!.Status);
        Assert.Equal(TokenStatuses.Revoked, (await store.FindByReferenceIdAsync("access"))!.Status);
        Assert.Equal(TokenStatuses.Revoked, (await store.FindByReferenceIdAsync("code"))!.Status);
        var next = new SchemataToken { Name = "next", ReferenceId = "next", Status = TokenStatuses.Valid };
        await store.CreateAsync(next);
        Assert.True(await store.TryRedeemAsync((await store.FindByReferenceIdAsync("next"))!));
        using var verify = host.Services.CreateScope();
        var persisted = await verify.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>()
            .ListAsync(q => q.Where(t => t.Authorization == grant.CanonicalName)).ToListAsync();
        Assert.Equal(3, persisted.Count);
        Assert.All(persisted, token => Assert.Equal(TokenStatuses.Revoked, token.Status));
        Assert.Equal(TokenStatuses.Redeemed, (await verify.ServiceProvider.GetRequiredService<RepositoryTokenStore>()
            .FindByReferenceIdAsync("next"))!.Status);
        Assert.Equal(stamp, (await verify.ServiceProvider.GetRequiredService<IRepository<SchemataApplication>>()
            .SingleOrDefaultAsync(q => q.Where(a => a.Name == "client")))!.Timestamp);
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Family_Exits_And_Publication_Rollback_Leave_One_Store_Usable(bool linq) {
        await using var host = await Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        Assert.False(await store.InvalidateFamilyAsync("missing"));
        Assert.False(await store.PublishFamilyAsync("missing", [new() { Name = "missing-member" }], false));
        var failure = new IOException("publication failed");
        var caught = await Assert.ThrowsAsync<IOException>(() => store.PublishFamilyAsync("rolled-back",
            [new() { Name = "rolled-back-member", Family = "rolled-back" }], true,
            beforeCommit: (_, _) => Task.FromException(failure)));
        Assert.Same(failure, caught);
        Assert.Null(await store.FindByNameAsync("rolled-back-member"));
        Assert.False(await store.IsFamilyActiveAsync("rolled-back"));
        await store.CreateAsync(new() { Name = "collision", ReferenceId = "collision" });
        await Assert.ThrowsAsync<AlreadyExistsException>(() => store.PublishFamilyAsync("collision-family",
            [new() { Name = "collision-member", ReferenceId = "collision" }], true));
        Assert.False(await store.IsFamilyActiveAsync("collision-family"));
        Assert.Null(await store.FindByNameAsync("collision-member"));
        Assert.True(await store.PublishFamilyAsync("live", [new() { Name = "member", Family = "live", Status = TokenStatuses.Valid }], true));
        Assert.True(await store.InvalidateFamilyAsync("live"));
        Assert.True(await store.InvalidateFamilyAsync("live"));
        Assert.False(await store.PublishFamilyAsync("live", [new() { Name = "rejected-member" }], false));
        await store.SetAsync("users/user", "slot", "key", "first", null);
        await store.SetAsync("users/user", "slot", "key", "second", null);
        Assert.Equal("second", (await store.GetAsync("users/user", "slot", "key"))!.Value);
        await store.RemoveAsync("users/user", "slot", "key");
        Assert.Null(await store.GetAsync("users/user", "slot", "key"));
        using var verify = host.Services.CreateScope();
        var rows = await verify.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>().ListAsync(q => q).ToListAsync();
        Assert.Equal(Schemata.Security.Skeleton.SecurityConstants.Statuses.FamilyInvalidated,
            Assert.Single(rows, t => t.FamilyKey == "live").Status);
        Assert.Equal(TokenStatuses.Revoked, Assert.Single(rows, t => t.Name == "member").Status);
        Assert.DoesNotContain(rows, t => t.FamilyKey == "rolled-back" || t.Name == "rejected-member");
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Family_Rotation_Loser_Revokes_The_Winner_Then_Continues(bool linq) {
        await using var host = await Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        Assert.True(await store.PublishFamilyAsync("rotation", [new() {
            Name = "predecessor", ReferenceId = "predecessor", Family = "rotation", Status = TokenStatuses.Valid,
        }], true));
        var stale = (await store.FindByReferenceIdAsync("predecessor"))!;
        using (var winnerScope = host.Services.CreateScope()) {
            var winner = winnerScope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
            Assert.True(await winner.RotateFamilyAsync((await winner.FindByReferenceIdAsync("predecessor"))!, [new() {
                Name = "winner", ReferenceId = "winner", Family = "rotation", Status = TokenStatuses.Valid,
            }]));
        }
        Assert.False(await store.RotateFamilyAsync(stale, [new() {
            Name = "loser", ReferenceId = "loser", Family = "rotation", Status = TokenStatuses.Valid,
        }]));
        Assert.False(await store.IsFamilyActiveAsync("rotation"));
        Assert.Equal(TokenStatuses.Revoked, (await store.FindByReferenceIdAsync("winner"))!.Status);
        Assert.Null(await store.FindByReferenceIdAsync("loser"));
        await store.CreateAsync(new() { Name = "independent", ReferenceId = "independent", Status = TokenStatuses.Valid });
        Assert.True(await store.TryRedeemAsync((await store.FindByReferenceIdAsync("independent"))!));
        Assert.Equal(TokenStatuses.Redeemed, (await store.FindByReferenceIdAsync("independent"))!.Status);
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Security_Publication_Rollback_Then_Create_Update_Delete_Uses_One_Store(bool linq) {
        await using var host = await Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<SecurityStore<SchemataSecurity>>();
        await Assert.ThrowsAsync<IOException>(() => store.CreateAsync(new() { Name = "failed" },
            beforeCommit: (_, _) => Task.FromException(new IOException("failed"))));
        Assert.Null(await store.FindByCanonicalNameAsync("securities/failed"));
        var security = (await store.CreateAsync(new() { Name = "secret", Value = "old" }))!;
        security.Value = "new";
        await store.UpdateAsync(security, beforeCommit: (_, _) => Task.CompletedTask);
        Assert.Equal("new", (await store.FindByCanonicalNameAsync(security.CanonicalName))!.Value);
        await store.DeleteAsync(security);
        Assert.Null(await store.FindByCanonicalNameAsync(security.CanonicalName));
    }

    public sealed class RaceExchange : ICodeExchangeAdvisor<SchemataApplication>
    {
        public Func<Task>? Run { get; set; }
        public int Order => AdviceCodeExchangeValidation.DefaultOrder + 1;
        public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, CodeExchangeContext<SchemataApplication> exchange, CancellationToken ct) {
            if (Run is { } run) {
                Run = null;
                await run();
            }
            return AdviseResult.Continue;
        }
    }

    public sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataToken>();
            model.Entity<SchemataSecurity>();
            model.Entity<SchemataScope>();
            model.Entity<SchemataApplication>();
            model.Entity<SchemataAuthorization>();
        }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    internal sealed class Host(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;
        public static async Task<Host> CreateAsync(bool linq) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-token-lifecycle-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            if (linq) {
                var schema = new MappingSchema();
                schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(schema);
                services.AddSingleton<Func<Connection>>(_ => () => new(options));
                using var db = new Connection(options);
                db.CreateTable<SchemataToken>();
                db.Execute("CREATE UNIQUE INDEX TokenReference ON SchemataTokens (ReferenceId)");
                db.Execute("CREATE UNIQUE INDEX TokenFamily ON SchemataTokens (FamilyKey)");
                db.Execute("CREATE UNIQUE INDEX TokenName ON SchemataTokens (Name)");
                db.Execute("CREATE UNIQUE INDEX TokenSlot ON SchemataTokens (Parent, Provider, Key)");
                db.CreateTable<SchemataSecurity>();
                db.CreateTable<SchemataScope>();
                db.CreateTable<SchemataApplication>();
                db.CreateTable<SchemataAuthorization>();
            } else {
                services.AddDbContextFactory<Context>(o => o.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            }
            Register<SchemataToken>(services, linq);
            Register<SchemataSecurity>(services, linq);
            Register<SchemataScope>(services, linq);
            Register<SchemataApplication>(services, linq);
            Register<SchemataAuthorization>(services, linq);
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(ResourceNameAdvisor<>)));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<RepositoryTokenStore>();
            services.AddScoped<ITokenStore<SchemataToken>>(sp => sp.GetRequiredService<RepositoryTokenStore>());
            services.AddScoped<SecurityStore<SchemataSecurity>>();
            services.AddScoped<IApplicationManager<SchemataApplication>, SchemataApplicationManager<SchemataApplication, SchemataAuthorization>>();
            services.AddScoped<IClientAuthentication<SchemataApplication>, NoneAuthentication<SchemataApplication>>();
            services.AddScoped<IClientAuthenticationService<SchemataApplication>, ClientAuthenticationService<SchemataApplication>>();
            services.AddScoped<ClientAssertionChannel>();
            services.AddSingleton(Options.Create(new SchemataAuthorizationOptions()));
            services.AddSingleton(Options.Create(new CodeFlowOptions()));
            services.AddSingleton(Options.Create(new JsonSerializerOptions()));
            services.AddScoped<AuthorizationCodeHandler<SchemataApplication>>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<ICodeExchangeAdvisor<SchemataApplication>, AdviceCodeExchangeValidation<SchemataApplication>>());
            services.AddScoped<RaceExchange>();
            services.AddScoped<ICodeExchangeAdvisor<SchemataApplication>>(sp => sp.GetRequiredService<RaceExchange>());
            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var db = await root.GetRequiredService<IDbContextFactory<Context>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }
            return new(root, path);
        }
        private static void Register<T>(IServiceCollection services, bool linq) where T : class {
            if (linq) services.AddRepository<T, LinqToDbRepository<Connection, T>>();
            else services.AddRepository<T, EfCoreRepository<Context, T>>();
        }
        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            File.Delete(path);
        }
    }
}
