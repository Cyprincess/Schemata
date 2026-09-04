using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Security.Skeleton;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

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
    schema.Services.AddRepository<SchemataSecurity, EfCoreRepository<AuthorizationDbContext, SchemataSecurity>>();
    var resource = schema.UseResource();
    resource.MapHttp().Use<TestSubject>();
    resource.MapHttp().Use<SchemataAuthorization>();
    schema.Services.AddRepository<TestSubject, EfCoreRepository<AuthorizationDbContext, TestSubject>>();
    schema.Services.AddRepository<SchemataSubjectMapping, EfCoreRepository<AuthorizationDbContext, SchemataSubjectMapping>>();

    schema.UseWellKnown();
    schema.UseSecurity();
    var authorization = schema.UseAuthorization(o => {
        o.Issuer         = "https://localhost";
        o.InteractionUri = "https://localhost/interact";
    })
                              .UseCodeFlow()
                              .UseClientCredentialsFlow()
                              .UseRefreshTokenFlow()
                              .UseJwtBearerGrant()
                              .UseIntrospection()
                              .UseUserInfo()
                              .MapHttp();

    if (builder.Environment.EnvironmentName == "Dpop") {
        authorization.UseDemonstratingProofOfPossession(o => o.RequireForAllClients());
    }

    if (builder.Environment.EnvironmentName == "Rar") {
        authorization.UseRichAuthorizationRequests();
        schema.Services.AddSingleton<IAuthorizationDetailTypeDescriptor, Schemata.Authorization.Integration.Tests.PaymentInitiationDescriptor>();
    }

    if (builder.Environment.EnvironmentName is "Authenticated" or "Authorized") {
        authorization.WithAuthentication("ManagementTest");
    }

    if (builder.Environment.EnvironmentName == "Authorized") {
        authorization.WithAuthorization();
    }

    schema.UseAuthentication((AuthenticationBuilder _) => { });
    schema.Services.AddScoped<IAuthenticationContextProvider, TestAuthenticationContextProvider>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.EnsureCreatedAsync();

    var applications = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
    var securities   = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
    var verifier     = scope.ServiceProvider.GetRequiredService<ISecretVerifier>();

    async Task SeedIssuerKeyAsync(string name, string usage, string algorithm) {
        using var rsa = RSA.Create(2048);
        await securities.CreateAsync(new() {
            Parent    = SecurityParents.Issuer("https://localhost"),
            Name      = name,
            Kind      = SecurityConstants.Kinds.PrivateKey,
            Usage     = usage,
            Algorithm = algorithm,
            Kid       = $"eph-{Guid.NewGuid():n}",
            Value     = rsa.ExportPkcs8PrivateKeyPem(),
            Status    = SecurityConstants.Statuses.Valid,
        });
    }

    await SeedIssuerKeyAsync("issuer-signing", SecurityConstants.Usages.Signing, SecurityConstants.Algorithms.Rsa);
    await SeedIssuerKeyAsync("issuer-encryption", SecurityConstants.Usages.Encryption, SecurityConstants.Algorithms.Rsa);

    async Task SeedPasswordAsync(SchemataApplication app, string secret) {
        await securities.CreateAsync(new() {
            Parent    = SecurityParents.Application(app),
            Name      = app.ClientId,
            Kind      = SecurityConstants.Kinds.Password,
            Usage     = SecurityConstants.Usages.Authentication,
            Algorithm = SecurityConstants.Algorithms.Pbkdf2,
            Value     = await verifier.HashAsync(secret),
            Status    = SecurityConstants.Statuses.Valid,
        });
    }

    var testApp = new SchemataApplication {
        Name        = "test-client",
        ClientId    = "test-client",
        ClientType  = "confidential",
        Permissions = new List<string> { "e:/Connect/Token", "g:client_credentials" },
    };
    await SeedPasswordAsync(testApp, "test-secret");
    await applications.CreateAsync(testApp);

    var dpopApp = new SchemataApplication {
        Name        = "dpop-client",
        ClientId    = "dpop-client",
        ClientType  = "confidential",
        Permissions = new List<string> { "e:/Connect/Token", "g:client_credentials" },
        DpopBoundAccessTokens = true,
    };
    await SeedPasswordAsync(dpopApp, "dpop-secret");
    await applications.CreateAsync(dpopApp);

    var codeApp = new SchemataApplication {
        Name         = "code-client",
        ClientId     = "code-client",
        ClientType   = "confidential",
        RedirectUris = new List<string> { "https://localhost/callback" },
        Permissions  = new List<string> { "e:/Connect/Authorize", "e:/Connect/Token", "g:authorization_code", "g:refresh_token", "s:openid" },
    };
    await SeedPasswordAsync(codeApp, "code-secret");
    await applications.CreateAsync(codeApp);

    var jwtApp = new SchemataApplication {
        Name        = "jwt-client",
        ClientId    = "jwt-client",
        ClientType  = "confidential",
        Permissions = new List<string> {
            "e:/Connect/Token",
            "g:urn:ietf:params:oauth:grant-type:jwt-bearer",
            "s:api:read",
        },
    };
    await SeedPasswordAsync(jwtApp, "jwt-secret");
    await applications.CreateAsync(jwtApp);

    var introspectApp = new SchemataApplication {
        Name        = "introspect-client",
        ClientId    = "introspect-client",
        ClientType  = "confidential",
        Permissions = new List<string> { "e:/Connect/Introspect" },
    };
    await SeedPasswordAsync(introspectApp, "introspect-secret");
    await applications.CreateAsync(introspectApp);

    var browserApp = new SchemataApplication {
        Name         = "browser-client",
        ClientId     = "browser-client",
        ClientType   = "public",
        RedirectUris = new List<string> { "https://localhost/callback" },
        Permissions  = new List<string> { "e:/Connect/Authorize", "g:authorization_code" },
    };
    await applications.CreateAsync(browserApp);
}

app.MapGet(
    "/test/whoami",
    [Authorize(Policy = SchemataAuthorizationPolicies.Profile)] (
        HttpContext context
    ) => context.User.Identity?.AuthenticationType ?? string.Empty);

app.Run();

namespace Schemata.Authorization.Integration.Tests
{
    public partial class Program;
}
