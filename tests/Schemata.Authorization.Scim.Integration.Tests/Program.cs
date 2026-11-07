using System;
using System.Linq;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Scim.Integration.Tests;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Foundation;
using Schemata.Scim.Entities;
using Schemata.Scim.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
var connectionString = $"Data Source=authorization-scim-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";
using var connection = new SqliteConnection(connectionString);
connection.Open();
var environment = builder.Environment.EnvironmentName;
var providerEvidence = environment.StartsWith("Provider", StringComparison.Ordinal);
var linq = environment.Contains("LinqToDb", StringComparison.Ordinal);
DataOptions? linqOptions = null;
var denied = environment.Contains("Denied", StringComparison.Ordinal);

builder.UseSchemata(schema => {
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(ResourceNameAdvisor<>)));
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<ScimProvisioningBinding>, FailingBindingAdvisor>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryUpdateAdvisor<ScimProvisioningBinding>, FailingBindingAdvisor>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataSecurity>, FailingSecurityAdvisor>());
    schema.Services.AddMemoryCacheProvider();
    schema.Services.TryAddScoped<IScimProvisioningDomainProvider, TestProvisioningDomainProvider>();
    schema.Services.TryAddScoped<IScimProvisioningAuthorizer>(_ => new TestProvisioningAuthorizer(denied));
    schema.Services.AddScoped<IAuthenticationContextProvider, TestAuthenticationContextProvider>();
    schema.Services.AddSingleton<IInitialAccessTokenValidator, TestInitialAccessTokenValidator>();

    schema.UseMapster().Map<SchemataApplication, SchemataApplication>();
    schema.UseMapster().Map<SchemataScope, SchemataScope>();
    schema.UseMapster().Map<SchemataToken, SchemataToken>();

    if (linq) {
        var mapping = new MappingSchema();
        mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
        linqOptions = new DataOptions().UseSQLite(connectionString).UseMappingSchema(mapping);
        var options = linqOptions;
        schema.Services.AddSingleton<Func<AuthorizationScimConnection>>(_ => () => new(options));
        if (providerEvidence) {
            schema.Services.AddRepository<ProviderApplication, LinqToDbRepository<AuthorizationScimConnection, ProviderApplication>>();
        } else {
            schema.Services.AddRepository<SchemataApplication, LinqToDbRepository<AuthorizationScimConnection, SchemataApplication>>();
        }
        schema.Services.AddRepository<SchemataAuthorization, LinqToDbRepository<AuthorizationScimConnection, SchemataAuthorization>>();
        schema.Services.AddRepository<SchemataScope, LinqToDbRepository<AuthorizationScimConnection, SchemataScope>>();
        schema.Services.AddRepository<SchemataToken, LinqToDbRepository<AuthorizationScimConnection, SchemataToken>>();
        schema.Services.AddRepository<SchemataSecurity, LinqToDbRepository<AuthorizationScimConnection, SchemataSecurity>>();
        schema.Services.AddRepository<SchemataSubjectMapping, LinqToDbRepository<AuthorizationScimConnection, SchemataSubjectMapping>>();
        schema.Services.AddRepository<ScimProvisioningBinding, LinqToDbRepository<AuthorizationScimConnection, ScimProvisioningBinding>>();
        schema.Services.AddRepository<TestSubject, LinqToDbRepository<AuthorizationScimConnection, TestSubject>>();
    } else {
        if (providerEvidence) {
            schema.Services.AddRepository<ProviderApplication, EfCoreRepository<AuthorizationScimDbContext, ProviderApplication>>();
        } else {
            schema.Services.AddRepository<SchemataApplication, EfCoreRepository<AuthorizationScimDbContext, SchemataApplication>>();
        }
        schema.Services.AddDbContextFactory<AuthorizationScimDbContext>((_, db) => {
            db.UseSqlite(connection);
            db.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
        });
        schema.Services.AddRepository<SchemataAuthorization, EfCoreRepository<AuthorizationScimDbContext, SchemataAuthorization>>();
        schema.Services.AddRepository<SchemataScope, EfCoreRepository<AuthorizationScimDbContext, SchemataScope>>();
        schema.Services.AddRepository<SchemataToken, EfCoreRepository<AuthorizationScimDbContext, SchemataToken>>();
        schema.Services.AddRepository<SchemataSecurity, EfCoreRepository<AuthorizationScimDbContext, SchemataSecurity>>();
        schema.Services.AddRepository<SchemataSubjectMapping, EfCoreRepository<AuthorizationScimDbContext, SchemataSubjectMapping>>();
        schema.Services.AddRepository<ScimProvisioningBinding, EfCoreRepository<AuthorizationScimDbContext, ScimProvisioningBinding>>();
        schema.Services.AddRepository<TestSubject, EfCoreRepository<AuthorizationScimDbContext, TestSubject>>();
    }
    if (environment == "Combined") {
        schema.Services.AddRepository<SchemataUser, EfCoreRepository<AuthorizationScimDbContext, SchemataUser>>();
        schema.Services.AddRepository<SchemataRole, EfCoreRepository<AuthorizationScimDbContext, SchemataRole>>();
        schema.Services.AddRepository<SchemataUserClaim, EfCoreRepository<AuthorizationScimDbContext, SchemataUserClaim>>();
        schema.Services.AddRepository<SchemataUserRole, EfCoreRepository<AuthorizationScimDbContext, SchemataUserRole>>();
        schema.Services.AddRepository<SchemataUserLogin, EfCoreRepository<AuthorizationScimDbContext, SchemataUserLogin>>();
        schema.Services.AddRepository<SchemataUserToken, EfCoreRepository<AuthorizationScimDbContext, SchemataUserToken>>();
        schema.Services.AddRepository<SchemataRoleClaim, EfCoreRepository<AuthorizationScimDbContext, SchemataRoleClaim>>();
        schema.UseIdentity().UseScim();
    }

    schema.UseResource().MapHttp().Use<TestSubject>();
    schema.UseWellKnown();
    schema.UseSecurity();
    if (providerEvidence) {
        schema.UseAuthorization<ProviderApplication, SchemataAuthorization, SchemataScope, SchemataSubjectMapping>(options => {
                  options.Issuer = "https://localhost";
                  options.InteractionUri = "https://localhost/interact";
              })
              .UseAuthorizationCodeFlow()
              .UseRefreshTokenFlow()
              .UseClientCredentialsFlow()
              .UseDynamicClientRegistration()
              .UseRegistrationReplace()
              .UseRegistrationDelete()
              .UseScim();
    } else {
        var authorization = schema.UseAuthorization(options => {
                  options.Issuer = "https://localhost";
                  options.InteractionUri = "https://localhost/interact";
              })
              .UseAuthorizationCodeFlow()
              .UseRefreshTokenFlow()
              .UseClientCredentialsFlow()
              .UseDynamicClientRegistration()
              .UseRegistrationReplace()
              .UseRegistrationDelete()
              .UseScim();
        if (environment != "WithoutJar") {
            authorization.UseJwtSecuredAuthorizationRequests(options => {
                options.SigningAlgorithms.Add("RS256");
                options.SigningAlgorithms.Add("RS384");
                options.EncryptionAlgorithms.Add("RSA-OAEP");
                options.ContentEncryptionAlgorithms.Add("A128CBC-HS256");
            });
        }
    }

    schema.UseAuthentication(
        authentication => authentication.AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, null),
        options => {
            options.DefaultScheme = TestAuthenticationHandler.SchemeName;
            options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
        },
        null);
});

var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    if (linqOptions is not null) {
        using var db = new AuthorizationScimConnection(linqOptions);
        if (providerEvidence) db.CreateTable<ProviderApplication>("ProviderApplications");
        else db.CreateTable<SchemataApplication>();
        db.CreateTable<SchemataAuthorization>();
        db.CreateTable<SchemataScope>();
        db.CreateTable<SchemataToken>();
        db.CreateTable<SchemataSecurity>();
        db.CreateTable<SchemataSubjectMapping>();
        db.CreateTable<ScimProvisioningBinding>();
        db.CreateTable<TestSubject>();
    } else {
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    using var rsa = RSA.Create(2048);
    var securities = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
    await securities.CreateAsync(new() {
        Parent = SecurityParents.Issuer("https://localhost"),
        Name = "issuer-signing",
        Kind = SecurityConstants.Kinds.PrivateKey,
        Usage = SecurityConstants.Usages.Signing,
        Algorithm = SecurityConstants.Algorithms.Rsa,
        Kid = "authorization-scim-signing",
        Value = rsa.ExportPkcs8PrivateKeyPem(),
        Status = SecurityConstants.Statuses.Valid,
    });
    using var encryption = RSA.Create(2048);
    await securities.CreateAsync(new() {
        Parent = SecurityParents.Issuer("https://localhost"),
        Name = "issuer-encryption",
        Kind = SecurityConstants.Kinds.PrivateKey,
        Usage = SecurityConstants.Usages.Encryption,
        Algorithm = SecurityConstants.Algorithms.Rsa,
        Kid = "authorization-scim-encryption",
        Value = encryption.ExportPkcs8PrivateKeyPem(),
        Status = SecurityConstants.Statuses.Valid,
    });
}

app.Run();

namespace Schemata.Authorization.Scim.Integration.Tests
{
    public partial class Program;

    public sealed class AuthorizationScimConnection(DataOptions options) : DataConnection(options);

    [System.ComponentModel.DataAnnotations.Schema.Table("ProviderApplications")]
    [CanonicalName("applications/{application}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class ProviderApplication : SchemataApplication;

    public sealed class AuthorizationScimDbContext(DbContextOptions<AuthorizationScimDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataApplication> Applications { get; set; } = null!;
        public DbSet<ProviderApplication> ProviderApplications { get; set; } = null!;
        public DbSet<SchemataAuthorization> Authorizations { get; set; } = null!;
        public DbSet<SchemataScope> Scopes { get; set; } = null!;
        public DbSet<SchemataSubjectMapping> SubjectMappings { get; set; } = null!;
        public DbSet<SchemataSecurity> Securities { get; set; } = null!;
        public DbSet<SchemataToken> Tokens { get; set; } = null!;
        public DbSet<ScimProvisioningBinding> ProvisioningBindings { get; set; } = null!;
        public DbSet<TestSubject> Subjects { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<ProviderApplication>().HasBaseType((Type?)null);
            model.Entity<SchemataUser>();
            model.Entity<SchemataRole>();
            model.Entity<SchemataUserClaim>();
            model.Entity<SchemataUserRole>();
            model.Entity<SchemataUserLogin>();
            model.Entity<SchemataUserToken>();
            model.Entity<SchemataRoleClaim>();
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("TestSubjects")]
    [CanonicalName("users/{subject}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public class TestSubject : IIdentifier, ICanonicalName, ITimestamp
    {
        public virtual Guid Uid { get; set; }
        public virtual string? Name { get; set; }
        public virtual string? CanonicalName { get; set; }
        public virtual DateTime? CreateTime { get; set; }
        public virtual DateTime? UpdateTime { get; set; }
    }

    internal sealed class ResourceNameAdvisor<TEntity> : IRepositoryAddAdvisor<TEntity> where TEntity : class
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext ctx,
            IRepository<TEntity> repository,
            TEntity entity,
            MutationOptions options,
            CancellationToken ct
        ) {
            if (entity is ICanonicalName named) {
                named.Name ??= $"resource-{Guid.NewGuid():n}";
            }

            return Task.FromResult(AdviseResult.Continue);
        }
    }

    internal sealed class FailingBindingAdvisor : IRepositoryAddAdvisor<ScimProvisioningBinding>, IRepositoryUpdateAdvisor<ScimProvisioningBinding>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext ctx,
            IRepository<ScimProvisioningBinding> repository,
            ScimProvisioningBinding entity,
            MutationOptions options,
            CancellationToken ct
        ) {
            if (entity.ExternalId == "fail-binding") {
                throw new InvalidOperationException("The binding write failed.");
            }

            return Task.FromResult(AdviseResult.Continue);
        }
    }

    internal sealed class FailingSecurityAdvisor : IRepositoryAddAdvisor<SchemataSecurity>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext ctx,
            IRepository<SchemataSecurity> repository,
            SchemataSecurity entity,
            MutationOptions options,
            CancellationToken ct
        ) {
            if (entity.Kind == SecurityConstants.Kinds.Jwks && entity.Value?.Contains("fail-security", StringComparison.Ordinal) == true) {
                throw new InvalidOperationException("The security write failed.");
            }

            return Task.FromResult(AdviseResult.Continue);
        }
    }

    internal sealed class TestProvisioningDomainProvider : IScimProvisioningDomainProvider
    {
        public Task<string> GetDomainAsync(ClaimsPrincipal principal, CancellationToken ct) {
            return Task.FromResult(principal.FindFirst("scim_domain")?.Value ?? "domain-a");
        }
    }

    internal sealed class TestProvisioningAuthorizer(bool denied) : IScimProvisioningAuthorizer
    {
        public Task<bool> CanWriteAsync(ClaimsPrincipal principal, string resourceType, string operation, CancellationToken ct) {
            return Task.FromResult(!denied);
        }

        public Task<bool> CanWritePermissionsAsync(ClaimsPrincipal principal, CancellationToken ct) {
            return Task.FromResult(!denied && principal.HasClaim("scim_permissions", "allow"));
        }
    }

    internal sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "AuthorizationScimTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (!Request.Headers.ContainsKey("X-Test-Scim")) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var domain = Request.Headers["X-Test-Domain"].FirstOrDefault() ?? "domain-a";
            var permissions = Request.Headers["X-Test-Permissions"].FirstOrDefault();
            var claims = new[] {
                new Claim(ClaimTypes.Name, "provisioner"),
                new Claim("sub", "users/u-1"),
                new Claim("scim_domain", domain),
            }.Concat(permissions == "allow" ? [new Claim("scim_permissions", "allow")] : []);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new(principal, SchemeName)));
        }
    }

    internal sealed class TestInitialAccessTokenValidator : IInitialAccessTokenValidator
    {
        public Task<bool> ValidateAsync(string? token, CancellationToken ct) {
            return Task.FromResult(token == "initial-token");
        }
    }

    internal sealed class TestAuthenticationContextProvider : IAuthenticationContextProvider
    {
        public Task<AuthenticationContext> GetContextAsync(ClaimsPrincipal? principal, CancellationToken ct = default) {
            return Task.FromResult(new AuthenticationContext(null, [], null));
        }
    }
}
