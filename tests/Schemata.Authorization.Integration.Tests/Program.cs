using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.EntityFrameworkCore;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();

builder.UseSchemata(schema => {
    schema.UseMapster().Map<SchemataApplication, SchemataApplication>();
    schema.UseMapster().Map<SchemataScope, SchemataScope>();
    schema.UseMapster().Map<SchemataToken, SchemataToken>();
    schema.Services.AddDistributedMemoryCache();
    schema.Services.AddDistributedCache();
    schema.Services
          .AddRepository<SchemataApplication, EfCoreRepository<AuthorizationDbContext, SchemataApplication>>()
          .UseEntityFrameworkCore<AuthorizationDbContext>((_, db) => {
              db.UseSqlite(connection);
              db.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
          })
          .WithUnitOfWork<AuthorizationDbContext>();
    schema.Services.AddRepository<SchemataAuthorization, EfCoreRepository<AuthorizationDbContext, SchemataAuthorization>>();
    schema.Services.AddRepository<SchemataScope, EfCoreRepository<AuthorizationDbContext, SchemataScope>>();
    schema.Services.AddRepository<SchemataToken, EfCoreRepository<AuthorizationDbContext, SchemataToken>>();
    schema.Services.AddRepository<SchemataSubjectMapping, EfCoreRepository<AuthorizationDbContext, SchemataSubjectMapping>>();

    schema.UseWellKnown();
    schema.UseSecurity();
    var authorization = schema.UseAuthorization(o => {
        o.Issuer         = "https://localhost";
        o.InteractionUri = "https://localhost/interact";
        o.AddEphemeralSigningKey();
        o.AddEphemeralEncryptionKey();
        o.PermitResponseType("code");
    })
                              .UseCodeFlow()
                              .UseClientCredentialsFlow()
                              .UseRefreshTokenFlow()
                              .UseIntrospection()
                              .MapHttp();

    if (builder.Environment.EnvironmentName is "Authenticated" or "Authorized") {
        authorization.WithAuthentication("ManagementTest");
    }

    if (builder.Environment.EnvironmentName == "Authorized") {
        authorization.WithAuthorization();
    }

    schema.UseAuthentication((AuthenticationBuilder _) => { });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.EnsureCreatedAsync();

    var applications = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
    var testApp = new SchemataApplication {
        Name        = "test-client",
        ClientId    = "test-client",
        ClientType  = "confidential",
        Permissions = new List<string> { "e:/Connect/Token", "g:client_credentials" },
    };
    await applications.SetClientSecretAsync(testApp, "test-secret");
    await applications.CreateAsync(testApp);

    var browserApp = new SchemataApplication {
        Name         = "browser-client",
        ClientId     = "browser-client",
        ClientType   = "public",
        RedirectUris = new List<string> { "https://localhost/callback" },
        Permissions  = new List<string> { "e:/Connect/Authorize", "g:authorization_code" },
    };
    await applications.CreateAsync(browserApp);
}

app.Run();

namespace Schemata.Authorization.Integration.Tests
{
    public partial class Program;
}
