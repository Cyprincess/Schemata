using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Integration.Tests.Fixtures;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Identity.Foundation;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Identity.Integration.Tests;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
var connectionString = $"Data Source=identity-management-{System.Guid.NewGuid():n};Mode=Memory;Cache=Shared";
using var connection = new SqliteConnection(connectionString);
connection.Open();
var tenanted = string.Equals(builder.Environment.EnvironmentName, "Tenanted", StringComparison.Ordinal);

builder.UseSchemata(schema => {
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(ResourceNameAdvisor<>)));
    schema.UseMapster().Map<SchemataUser, SchemataUser>();
    schema.UseMapster().Map<SchemataRole, SchemataRole>();
    schema.Services.AddMemoryCacheProvider();
    schema.Services.AddDbContextFactory<IdentityDbContext>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
    schema.Services.AddRepository<SchemataUser, EfCoreRepository<IdentityDbContext, SchemataUser>>();
    schema.Services.AddRepository<SchemataRole, EfCoreRepository<IdentityDbContext, SchemataRole>>();
    schema.Services.AddRepository<SchemataUserClaim, EfCoreRepository<IdentityDbContext, SchemataUserClaim>>();
    schema.Services.AddRepository<SchemataUserRole, EfCoreRepository<IdentityDbContext, SchemataUserRole>>();
    schema.Services.AddRepository<SchemataUserLogin, EfCoreRepository<IdentityDbContext, SchemataUserLogin>>();
    schema.Services.AddRepository<SchemataUserToken, EfCoreRepository<IdentityDbContext, SchemataUserToken>>();
    schema.Services.AddRepository<SchemataRoleClaim, EfCoreRepository<IdentityDbContext, SchemataRoleClaim>>();
    var identity = schema.UseIdentity().UseRegistration().UseAccountConfirmation().UsePasswordReset()
        .UsePasswordChange().UseEmailChange().UsePhoneNumberChange().UseTwoFactorAuthentication().MapHttp();
    schema.UseSecurity();
    if (builder.Environment.EnvironmentName == "Authenticated") identity.WithAuthentication("ManagementTest");
    schema.UseAuthentication((AuthenticationBuilder _) => { });
    if (tenanted) {
        var tenancy = schema.UseTenancy<TestTenantManager, SchemataTenant, SchemataTenantHost>();
        tenancy.Services.AddHttpContextAccessor();
        tenancy.Services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantResolver, TestTenantResolver>());
        tenancy.UseState(state => {
            state.CookiePrefix = "i.";
            state.UseBearer("i.bearer", _ => { });
        });
        tenancy.ForTenant((_, tenant, _) => {
            tenant.AddIdentityExecution<SchemataUser>();
            tenant.AddSingleton(Options.Create(new SchemataIdentityOptions { BearerScheme = "i.bearer" }));
        });
    }
});

var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContext();
    context.Database.EnsureCreated();
    var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
    await users.AddAsync(new() { Name = "test-user", UserName = "test-user" });
    await users.CommitAsync();
}
app.Run();

namespace Schemata.Identity.Integration.Tests { public partial class Program; }
