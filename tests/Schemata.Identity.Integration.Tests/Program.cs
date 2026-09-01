using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Integration.Tests;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
var connectionString = $"Data Source=identity-management-{System.Guid.NewGuid():n};Mode=Memory;Cache=Shared";
using var connection = new SqliteConnection(connectionString);
connection.Open();

builder.UseSchemata(schema => {
    schema.UseMapster().Map<SchemataUser, SchemataUser>();
    schema.UseMapster().Map<SchemataRole, SchemataRole>();
    schema.Services.AddDistributedMemoryCache();
    schema.Services.AddDistributedCache();
    schema.Services.AddDbContextFactory<IdentityDbContext>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
    schema.Services.AddRepository<SchemataUser, EfCoreRepository<IdentityDbContext, SchemataUser>>();
    schema.Services.AddRepository<SchemataRole, EfCoreRepository<IdentityDbContext, SchemataRole>>();
    schema.Services.AddRepository<SchemataUserClaim, EfCoreRepository<IdentityDbContext, SchemataUserClaim>>();
    schema.Services.AddRepository<SchemataUserRole, EfCoreRepository<IdentityDbContext, SchemataUserRole>>();
    schema.Services.AddRepository<SchemataUserLogin, EfCoreRepository<IdentityDbContext, SchemataUserLogin>>();
    schema.Services.AddRepository<SchemataUserToken, EfCoreRepository<IdentityDbContext, SchemataUserToken>>();
    schema.Services.AddRepository<SchemataRoleClaim, EfCoreRepository<IdentityDbContext, SchemataRoleClaim>>();
    var identity = schema.UseIdentity().MapHttp();
    schema.UseSecurity();
    if (builder.Environment.EnvironmentName == "Authenticated") identity.WithAuthentication("ManagementTest");
    schema.UseAuthentication((AuthenticationBuilder _) => { });
});

var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContext();
    context.Database.EnsureCreated();
    context.Users.Add(new() { Uid = System.Guid.NewGuid(), Name = "test-user", UserName = "test-user" });
    context.SaveChanges();
}
app.Run();

namespace Schemata.Identity.Integration.Tests { public partial class Program; }
