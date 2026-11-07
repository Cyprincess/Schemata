using System;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Foundation;
using Schemata.Identity.Scim.Integration.Tests;
using Schemata.Identity.Scim.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Scim.Entities;
using Schemata.Scim.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
var connectionString = $"Data Source=identity-scim-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";
using var connection = new SqliteConnection(connectionString);
connection.Open();
var environment = builder.Environment.EnvironmentName;
var providerEvidence = environment.StartsWith("Provider", StringComparison.Ordinal);
var linq = environment.Contains("LinqToDb", StringComparison.Ordinal);
DataOptions? linqOptions = null;
var denied = string.Equals(environment, "Denied", StringComparison.Ordinal);
var bindingFailure = environment.Contains("BindingFailure", StringComparison.Ordinal);
var missingCapability = string.Equals(environment, "MissingCapability", StringComparison.Ordinal);
var customRole = string.Equals(environment, "CustomRole", StringComparison.Ordinal) || providerEvidence;
var privilegeDenied = string.Equals(environment, "PrivilegeDenied", StringComparison.Ordinal);
var membershipFailure = environment.Contains("MembershipFailure", StringComparison.Ordinal);
var missingRoleCapability = string.Equals(environment, "MissingRoleCapability", StringComparison.Ordinal);
if (environment == "CustomPath") builder.Services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter, PathBaseStartup>());

builder.UseSchemata(schema => {
    if (environment == "CustomPath") schema.Services.Configure<Schemata.Scim.ScimServiceProviderOptions>(options => options.BasePath = "/directory");
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(ResourceNameAdvisor<>)));
    schema.Services.AddMemoryCacheProvider();
    schema.Services.TryAddScoped<IScimProvisioningDomainProvider, TestProvisioningDomainProvider>();
    if (denied) {
        schema.Services.TryAddScoped<IScimProvisioningAuthorizer, DeniedProvisioningAuthorizer>();
    }
    if (privilegeDenied) {
        schema.Services.TryAddScoped<IScimProvisioningAuthorizer, PrivilegeDeniedProvisioningAuthorizer>();
    }
    if (bindingFailure) {
        schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<ScimProvisioningBinding>, FailingBindingAdvisor>());
    }
    if (membershipFailure) {
        schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataUserRole>, FailingSecondMembershipAdvisor>());
    }
    if (missingCapability) {
        schema.Services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<Microsoft.AspNetCore.Identity.IUserStore<SchemataUser>>()));
    }
    if (missingRoleCapability) {
        schema.Services.Replace(ServiceDescriptor.Scoped(_ => Mock.Of<Microsoft.AspNetCore.Identity.IRoleStore<SchemataRole>>()));
    }

    if (linq) {
        var mapping = new MappingSchema();
        mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
        linqOptions = new DataOptions().UseSQLite(connectionString).UseMappingSchema(mapping);
        var options = linqOptions;
        schema.Services.AddSingleton<Func<IdentityScimConnection>>(_ => () => new(options));
        schema.Services.AddRepository<ScimProvisioningBinding, LinqToDbRepository<IdentityScimConnection, ScimProvisioningBinding>>();
        schema.Services.AddRepository<SchemataUser, LinqToDbRepository<IdentityScimConnection, SchemataUser>>();
        schema.Services.AddRepository<SchemataUserClaim, LinqToDbRepository<IdentityScimConnection, SchemataUserClaim>>();
        schema.Services.AddRepository<SchemataUserRole, LinqToDbRepository<IdentityScimConnection, SchemataUserRole>>();
        schema.Services.AddRepository<SchemataUserLogin, LinqToDbRepository<IdentityScimConnection, SchemataUserLogin>>();
        schema.Services.AddRepository<SchemataUserToken, LinqToDbRepository<IdentityScimConnection, SchemataUserToken>>();
        schema.Services.AddRepository<SchemataRoleClaim, LinqToDbRepository<IdentityScimConnection, SchemataRoleClaim>>();
        if (providerEvidence) {
            schema.Services.AddRepository<ProviderRole, LinqToDbRepository<IdentityScimConnection, ProviderRole>>();
            schema.UseIdentity<SchemataUser, ProviderRole>().UseScim();
        } else if (customRole) {
            schema.Services.AddRepository<CustomRole, LinqToDbRepository<IdentityScimConnection, CustomRole>>();
            schema.UseIdentity<SchemataUser, CustomRole>().UseScim();
        } else {
            schema.Services.AddRepository<SchemataRole, LinqToDbRepository<IdentityScimConnection, SchemataRole>>();
            schema.UseIdentity().UseScim();
        }
    } else if (providerEvidence) {
        schema.Services.AddRepository<ScimProvisioningBinding, EfCoreRepository<ProviderIdentityDbContext, ScimProvisioningBinding>>();
        schema.Services.AddDbContextFactory<ProviderIdentityDbContext>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        schema.Services.AddRepository<SchemataUser, EfCoreRepository<ProviderIdentityDbContext, SchemataUser>>();
        schema.Services.AddRepository<ProviderRole, EfCoreRepository<ProviderIdentityDbContext, ProviderRole>>();
        schema.Services.AddRepository<SchemataUserClaim, EfCoreRepository<ProviderIdentityDbContext, SchemataUserClaim>>();
        schema.Services.AddRepository<SchemataUserRole, EfCoreRepository<ProviderIdentityDbContext, SchemataUserRole>>();
        schema.Services.AddRepository<SchemataUserLogin, EfCoreRepository<ProviderIdentityDbContext, SchemataUserLogin>>();
        schema.Services.AddRepository<SchemataUserToken, EfCoreRepository<ProviderIdentityDbContext, SchemataUserToken>>();
        schema.Services.AddRepository<SchemataRoleClaim, EfCoreRepository<ProviderIdentityDbContext, SchemataRoleClaim>>();
        schema.UseIdentity<SchemataUser, ProviderRole>().UseScim();
    } else if (customRole) {
        schema.Services.AddRepository<ScimProvisioningBinding, EfCoreRepository<CustomRoleDbContext, ScimProvisioningBinding>>();
        schema.Services.AddDbContextFactory<CustomRoleDbContext>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        schema.Services.AddRepository<SchemataUser, EfCoreRepository<CustomRoleDbContext, SchemataUser>>();
        schema.Services.AddRepository<CustomRole, EfCoreRepository<CustomRoleDbContext, CustomRole>>();
        schema.Services.AddRepository<SchemataUserClaim, EfCoreRepository<CustomRoleDbContext, SchemataUserClaim>>();
        schema.Services.AddRepository<SchemataUserRole, EfCoreRepository<CustomRoleDbContext, SchemataUserRole>>();
        schema.Services.AddRepository<SchemataUserLogin, EfCoreRepository<CustomRoleDbContext, SchemataUserLogin>>();
        schema.Services.AddRepository<SchemataUserToken, EfCoreRepository<CustomRoleDbContext, SchemataUserToken>>();
        schema.Services.AddRepository<SchemataRoleClaim, EfCoreRepository<CustomRoleDbContext, SchemataRoleClaim>>();
        schema.UseIdentity<SchemataUser, CustomRole>().UseScim();
    } else {
        schema.Services.AddRepository<ScimProvisioningBinding, EfCoreRepository<IdentityDbContext, ScimProvisioningBinding>>();
        schema.Services.AddDbContextFactory<IdentityDbContext>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        schema.Services.AddRepository<SchemataUser, EfCoreRepository<IdentityDbContext, SchemataUser>>();
        schema.Services.AddRepository<SchemataRole, EfCoreRepository<IdentityDbContext, SchemataRole>>();
        schema.Services.AddRepository<SchemataUserClaim, EfCoreRepository<IdentityDbContext, SchemataUserClaim>>();
        schema.Services.AddRepository<SchemataUserRole, EfCoreRepository<IdentityDbContext, SchemataUserRole>>();
        schema.Services.AddRepository<SchemataUserLogin, EfCoreRepository<IdentityDbContext, SchemataUserLogin>>();
        schema.Services.AddRepository<SchemataUserToken, EfCoreRepository<IdentityDbContext, SchemataUserToken>>();
        schema.Services.AddRepository<SchemataRoleClaim, EfCoreRepository<IdentityDbContext, SchemataRoleClaim>>();
        schema.UseIdentity().UseScim();
    }

    schema.UseAuthentication(
        build => build.AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, null),
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
        using var db = new IdentityScimConnection(linqOptions);
        db.CreateTable<ScimProvisioningBinding>();
        db.CreateTable<SchemataUser>();
        if (providerEvidence) db.CreateTable<ProviderRole>("ProviderRoles");
        else if (customRole) db.CreateTable<CustomRole>("CustomRoles");
        else db.CreateTable<SchemataRole>();
        db.CreateTable<SchemataUserClaim>();
        db.CreateTable<SchemataUserRole>();
        db.CreateTable<SchemataUserLogin>();
        db.CreateTable<SchemataUserToken>();
        db.CreateTable<SchemataRoleClaim>();
    } else if (providerEvidence) {
        var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ProviderIdentityDbContext>>().CreateDbContext();
        context.Database.EnsureCreated();
    } else if (customRole) {
        var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CustomRoleDbContext>>().CreateDbContext();
        context.Database.EnsureCreated();
    } else {
        var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContext();
        context.Database.EnsureCreated();
    }
}

app.Run();

namespace Schemata.Identity.Scim.Integration.Tests
{
    public partial class Program;

    internal sealed class PathBaseStartup : IStartupFilter {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
            app.UsePathBase("/host");
            next(app);
        };
    }

    public sealed class IdentityScimConnection(DataOptions options) : DataConnection(options);

    [System.ComponentModel.DataAnnotations.Schema.Table("ProviderRoles")]
    [Schemata.Abstractions.Entities.CanonicalName("roles/{role}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class ProviderRole : SchemataRole;

    public sealed class ProviderIdentityDbContext(DbContextOptions<ProviderIdentityDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataUser> Users { get; set; } = null!;
        public DbSet<ProviderRole> Roles { get; set; } = null!;
        public DbSet<SchemataUserClaim> UserClaims { get; set; } = null!;
        public DbSet<SchemataUserRole> UserRoles { get; set; } = null!;
        public DbSet<SchemataUserLogin> UserLogins { get; set; } = null!;
        public DbSet<SchemataUserToken> UserTokens { get; set; } = null!;
        public DbSet<SchemataRoleClaim> RoleClaims { get; set; } = null!;
        public DbSet<ScimProvisioningBinding> ProvisioningBindings { get; set; } = null!;
    }
}
