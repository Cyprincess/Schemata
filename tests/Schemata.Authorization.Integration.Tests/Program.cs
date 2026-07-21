using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();

builder.UseSchemata(schema => {
    schema.Services
          .AddRepository<SchemataApplication, EfCoreRepository<AuthorizationDbContext, SchemataApplication>>()
          .UseEntityFrameworkCore<AuthorizationDbContext>((_, db) => db.UseSqlite(connection))
          .WithUnitOfWork<AuthorizationDbContext>();
    schema.Services.AddRepository<SchemataAuthorization, EfCoreRepository<AuthorizationDbContext, SchemataAuthorization>>();
    schema.Services.AddRepository<SchemataScope, EfCoreRepository<AuthorizationDbContext, SchemataScope>>();
    schema.Services.AddRepository<SchemataToken, EfCoreRepository<AuthorizationDbContext, SchemataToken>>();
    schema.Services.AddRepository<SchemataSubjectMapping, EfCoreRepository<AuthorizationDbContext, SchemataSubjectMapping>>();

    schema.UseWellKnown();
    schema.UseAuthorization(o => {
               o.Issuer = "https://localhost";
               o.AddEphemeralSigningKey();
               o.AddEphemeralEncryptionKey();
               o.PermitResponseType("code");
           })
           .UseClientCredentialsFlow()
           .UseRefreshTokenFlow()
           .UseIntrospection();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.EnsureCreatedAsync();

    var applications = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
    var testApp = new SchemataApplication {
        ClientId    = "test-client",
        ClientType  = "confidential",
        Permissions = new List<string> { "e:/Connect/Token", "g:client_credentials" },
    };
    await applications.SetClientSecretAsync(testApp, "test-secret");
    await applications.CreateAsync(testApp);
}

app.Run();

namespace Schemata.Authorization.Integration.Tests
{
    public partial class Program;
}
