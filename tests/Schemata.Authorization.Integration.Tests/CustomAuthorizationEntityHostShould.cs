using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Foundation.Mutations;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Security.Skeleton.Entities;
using System.Threading;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using PrimaryKey = Schemata.Abstractions.Entities.PrimaryKeyAttribute;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public class CustomAuthorizationEntityHostShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_Map_And_Cascade_Host_Clients_Through_UseAuthorization(bool linq) {
        await using var host = await Host.CreateAsync(linq);

        string applicationName;
        using (var scope = host.Services.CreateScope()) {
            var services = scope.ServiceProvider;
            var mutation = services.GetRequiredService<IResourceMutation<Application>>();
            Assert.IsType<ApplicationResourceMutation<Application, Grant, SubjectMapping, SchemataSecurity, SchemataToken>>(mutation);

            var application = new Application {
                Name         = "custom-client",
                ClientId     = "custom-client",
                RedirectUris = ["https://client.example/callback"],
            };
            await mutation.CreateAsync(application);
            applicationName = application.CanonicalName!;
            Assert.Equal("applications/custom-client", applicationName);

            var grants = services.GetRequiredService<IRepository<Grant>>();
            await grants.AddAsync(new() { Name = "custom-grant", Application = applicationName });
            await grants.CommitAsync();

            var tokens = services.GetRequiredService<IRepository<SchemataToken>>();
            await tokens.AddAsync(new() { Name = "custom-token", Application = applicationName });
            await tokens.CommitAsync();
        }

        using (var scope = host.Services.CreateScope()) {
            var services = scope.ServiceProvider;
            var applications = services.GetRequiredService<IRepository<Application>>();
            var application  = await applications.SingleOrDefaultAsync(q => q.Where(a => a.Name == "custom-client"));
            Assert.NotNull(application);

            var translator = services.GetRequiredService<IPairwiseSubjectTranslator>();
            var pairwise   = await translator.ToPairwiseAsync("users/alice", Caller("custom-client"));
            Assert.NotNull(pairwise);
            Assert.NotEqual("users/alice", pairwise);

            var mappings = services.GetRequiredService<IRepository<SubjectMapping>>();
            var mapping  = await mappings.SingleOrDefaultAsync(q => q.Where(m => m.Application == applicationName));
            Assert.NotNull(mapping);
            Assert.Equal("users/alice", mapping!.Subject);
            Assert.Equal(pairwise, mapping.PairwiseSubject);
        }

        using (var scope = host.Services.CreateScope()) {
            var services    = scope.ServiceProvider;
            var application = await services.GetRequiredService<IRepository<Application>>()
                                            .SingleOrDefaultAsync(q => q.Where(a => a.Name == "custom-client"));
            await services.GetRequiredService<IResourceMutation<Application>>().DeleteAsync(application!);
        }

        using (var scope = host.Services.CreateScope()) {
            var services = scope.ServiceProvider;
            Assert.Equal(0, await services.GetRequiredService<IRepository<Application>>().CountAsync(q => q));
            Assert.Equal(0, await services.GetRequiredService<IRepository<Grant>>().CountAsync(q => q));
            Assert.Equal(0, await services.GetRequiredService<IRepository<SchemataToken>>().CountAsync(q => q));
            Assert.Equal(0, await services.GetRequiredService<IRepository<SubjectMapping>>().CountAsync(q => q));
            Assert.Equal(0, await services.GetRequiredService<IRepository<SchemataApplication>>().CountAsync(q => q));
            Assert.Equal(0, await services.GetRequiredService<IRepository<SchemataAuthorization>>().CountAsync(q => q));
            Assert.Equal(0, await services.GetRequiredService<IRepository<SchemataScope>>().CountAsync(q => q));
            Assert.Equal(0, await services.GetRequiredService<IRepository<SchemataSubjectMapping>>().CountAsync(q => q));
        }
    }

    private static System.Security.Claims.ClaimsPrincipal Caller(string clientId) {
        return new(new System.Security.Claims.ClaimsIdentity(
            [new(Claims.ClientId, clientId)], "grant"));
    }

    private sealed class NameSubjectMapping : IRepositoryAddAdvisor<SubjectMapping> {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 100;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext              ctx,
            IRepository<SubjectMapping> repository,
            SubjectMapping              entity,
            MutationOptions      options,
            CancellationToken           ct
        ) {
            entity.Name ??= $"{entity.Application}:{entity.PairwiseSubject}";
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("CustomApplications")]
    [CanonicalName("applications/{application}")]
    [PrimaryKey(nameof(Uid))]
    public class Application : SchemataApplication, ISoftDelete
    {
        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime  { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("CustomGrants")]
    [CanonicalName("authorizations/{authorization}")]
    [PrimaryKey(nameof(Uid))]
    public class Grant : SchemataAuthorization, ISoftDelete
    {
        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime  { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("CustomScopes")]
    [CanonicalName("scopes/{scope}")]
    [PrimaryKey(nameof(Uid))]
    public class Scope : SchemataScope;

    [System.ComponentModel.DataAnnotations.Schema.Table("CustomSubjectMappings")]
    [CanonicalName("subjectMappings/{subjectMapping}")]
    [PrimaryKey(nameof(Uid))]
    public class SubjectMapping : SchemataSubjectMapping;

    public sealed class Context(DbContextOptions<Context> options) : DbContext(options) {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<Application>().HasBaseType((Type?)null);
            model.Entity<Grant>().HasBaseType((Type?)null);
            model.Entity<Scope>().HasBaseType((Type?)null);
            model.Entity<SubjectMapping>().HasBaseType((Type?)null);
            model.Entity<SchemataApplication>();
            model.Entity<SchemataAuthorization>();
            model.Entity<SchemataScope>();
            model.Entity<SchemataSubjectMapping>();
            model.Entity<SchemataToken>();
            model.Entity<SchemataSecurity>();
        }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    private sealed class Host(WebApplication app, string path) : IAsyncDisposable {
        public IServiceProvider Services => app.Services;

        internal static async Task<Host> CreateAsync(bool linq) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-custom-host-{Guid.NewGuid():N}.db");
            var builder = WebApplication.CreateBuilder();
            if (linq) {
                var mappings = new MappingSchema();
                mappings.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(mappings);
                builder.Services.AddSingleton<Func<Connection>>(_ => () => new(options));
                using var db = new Connection(options);
                db.CreateTable<Application>();
                db.CreateTable<Grant>();
                db.CreateTable<Scope>();
                db.CreateTable<SubjectMapping>();
                db.CreateTable<SchemataApplication>();
                db.CreateTable<SchemataAuthorization>();
                db.CreateTable<SchemataScope>();
                db.CreateTable<SchemataSubjectMapping>();
                db.CreateTable<SchemataToken>();
                db.CreateTable<SchemataSecurity>();
            } else {
                builder.Services.AddDbContextFactory<Context>(o => o.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            }

            builder.UseSchemata(schema => {
                Register<Application>(schema.Services, linq);
                Register<Grant>(schema.Services, linq);
                Register<Scope>(schema.Services, linq);
                Register<SubjectMapping>(schema.Services, linq);
                Register<SchemataApplication>(schema.Services, linq);
                Register<SchemataAuthorization>(schema.Services, linq);
                Register<SchemataScope>(schema.Services, linq);
                Register<SchemataSubjectMapping>(schema.Services, linq);
                Register<SchemataToken>(schema.Services, linq);
                Register<SchemataSecurity>(schema.Services, linq);
                schema.Services.TryAddEnumerable(
                    ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SubjectMapping>, NameSubjectMapping>());

                schema.UseSecurity();
                schema.UseAuthorization<Application, Grant, Scope, SubjectMapping>(o => {
                          o.Issuer       = "https://as.example";
                          o.SubjectType  = SubjectTypes.Pairwise;
                          o.PairwiseSalt = "custom-salt";
                      })
                      .UsePairwiseSubjects();
            });

            var app = builder.Build();
            if (!linq) {
                await using var db = await app.Services.GetRequiredService<IDbContextFactory<Context>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }

            return new(app, path);
        }

        private static void Register<T>(IServiceCollection services, bool linq) where T : class {
            if (linq) services.AddRepository<T, LinqToDbRepository<Connection, T>>();
            else services.AddRepository<T, EfCoreRepository<Context, T>>();
        }

        public async ValueTask DisposeAsync() {
            await app.DisposeAsync();
            File.Delete(path);
        }
    }
}
