using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Foundation;
using Schemata.Identity.Scim.Features;
using Schemata.Identity.Scim.Integration.Tests;
using Schemata.Identity.Scim.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton.Entities;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
var connectionString = $"Data Source=identity-scim-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";
using var connection = new SqliteConnection(connectionString);
connection.Open();
var denied            = string.Equals(builder.Environment.EnvironmentName, "Denied", StringComparison.Ordinal);
var scimPolicy        = string.Equals(builder.Environment.EnvironmentName, "ScimPolicy", StringComparison.Ordinal);
var limited           = string.Equals(builder.Environment.EnvironmentName, "Limited", StringComparison.Ordinal);
var membershipFailure = string.Equals(builder.Environment.EnvironmentName, "MembershipFailure", StringComparison.Ordinal);
var customRole        = string.Equals(builder.Environment.EnvironmentName, "CustomRole", StringComparison.Ordinal);

builder.UseSchemata(schema => {
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(ResourceNameAdvisor<>)));
    if (membershipFailure) {
        schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataUserRole>, FailingMembershipAdvisor>());
    }
    schema.Services.AddMemoryCacheProvider();
    if (customRole) {
        schema.Services.AddDbContextFactory<CustomRoleDbContext>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        schema.Services.AddRepository<SchemataUser, EfCoreRepository<CustomRoleDbContext, SchemataUser>>();
        schema.Services.AddRepository<CustomRole, EfCoreRepository<CustomRoleDbContext, CustomRole>>();
        schema.Services.AddRepository<SchemataUserClaim, EfCoreRepository<CustomRoleDbContext, SchemataUserClaim>>();
        schema.Services.AddRepository<SchemataUserRole, EfCoreRepository<CustomRoleDbContext, SchemataUserRole>>();
        schema.Services.AddRepository<SchemataUserLogin, EfCoreRepository<CustomRoleDbContext, SchemataUserLogin>>();
        schema.Services.AddRepository<SchemataUserToken, EfCoreRepository<CustomRoleDbContext, SchemataUserToken>>();
        schema.Services.AddRepository<SchemataRoleClaim, EfCoreRepository<CustomRoleDbContext, SchemataRoleClaim>>();
        schema.UseIdentity<SchemataUser, CustomRole>();
    } else {
        schema.Services.AddDbContextFactory<IdentityDbContext>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        schema.Services.AddRepository<SchemataUser, EfCoreRepository<IdentityDbContext, SchemataUser>>();
        schema.Services.AddRepository<SchemataRole, EfCoreRepository<IdentityDbContext, SchemataRole>>();
        schema.Services.AddRepository<SchemataUserClaim, EfCoreRepository<IdentityDbContext, SchemataUserClaim>>();
        schema.Services.AddRepository<SchemataUserRole, EfCoreRepository<IdentityDbContext, SchemataUserRole>>();
        schema.Services.AddRepository<SchemataUserLogin, EfCoreRepository<IdentityDbContext, SchemataUserLogin>>();
        schema.Services.AddRepository<SchemataUserToken, EfCoreRepository<IdentityDbContext, SchemataUserToken>>();
        schema.Services.AddRepository<SchemataRoleClaim, EfCoreRepository<IdentityDbContext, SchemataRoleClaim>>();
        schema.UseIdentity();
    }

    schema.UseIdentityScim<SchemataUser>();

    if (scimPolicy) {
        schema.Configure<SchemataIdentityScimOptions>(options => options.AuthorizationPolicy = "ScimProvisioning");
    }

    if (limited) {
        schema.Configure<SchemataIdentityScimOptions>(options => options.MaxPageSize = 2);
    }

    Action<AuthorizationOptions>? authorize = null;
    if (denied) {
        authorize = options => options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireClaim("scim", "provision")
            .Build();
    }

    if (scimPolicy) {
        authorize = options => options.AddPolicy("ScimProvisioning",
                                                policy => policy.RequireAuthenticatedUser().RequireClaim("scim", "provision"));
    }

    schema.UseAuthentication(
        build => build.AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, null),
        options => {
            options.DefaultScheme             = TestAuthenticationHandler.SchemeName;
            options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
            options.DefaultChallengeScheme    = TestAuthenticationHandler.SchemeName;
        },
        authorize);
});

var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    if (customRole) {
        var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CustomRoleDbContext>>().CreateDbContext();
        context.Database.EnsureCreated();
    } else {
        var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContext();
        context.Database.EnsureCreated();
    }
}

app.Run();

namespace Schemata.Identity.Scim.Integration.Tests { public partial class Program; }
