using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository.Advisors;
using Schemata.Authorization.Identity;
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
var useUserInfo = builder.Environment.EnvironmentName is not ("NoUserInfo" or "NoFlows");
var useFlows    = builder.Environment.EnvironmentName is not ("NoFlows" or "EndSessionOnly");
var useDeviceFlow = builder.Environment.EnvironmentName == "DeviceFlow";
var issuer        = builder.Environment.EnvironmentName == "IssuerPath" ? "https://localhost/issuer1" : "https://localhost";
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();

builder.UseSchemata(schema => {
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataApplication>, ResourceNameAdvisor<SchemataApplication>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataAuthorization>, ResourceNameAdvisor<SchemataAuthorization>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataScope>, ResourceNameAdvisor<SchemataScope>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataToken>, ResourceNameAdvisor<SchemataToken>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataSecurity>, ResourceNameAdvisor<SchemataSecurity>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<TestSubject>, ResourceNameAdvisor<TestSubject>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataSubjectMapping>, ResourceNameAdvisor<SchemataSubjectMapping>>());
    schema.UseMapster().Map<SchemataApplication, SchemataApplication>();
    schema.UseMapster().Map<SchemataScope, SchemataScope>();
    schema.UseMapster().Map<SchemataToken, SchemataToken>();
    schema.Services.AddMemoryCacheProvider();
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
    schema.Services.AddSingleton<IInitialAccessTokenValidator>(new Schemata.Authorization.Integration.Tests.TestInitialAccessTokenValidator());
    var authorization = schema.UseAuthorization(o => {
        o.Issuer         = issuer;
        o.InteractionUri = "https://localhost/interact";
        if (useDeviceFlow) {
            o.DeviceVerificationUri = "https://localhost/device";
        }

        // RFC 7662 §2: the resources introspect-client represents in these scenarios — the
        // issuer/default resource, the RFC 8707 calendar/contacts pair, and the Native SSO
        // exchange targets.
        o.IntrospectionResourceAudiences["introspect-client"] = [
            "https://localhost",
            "https://cal.example.com/",
            "https://contacts.example.com/",
            "https://calendar.example/api",
            "https://contacts.example/api",
        ];
        o.IntrospectionResourceAudiences["foreign-introspect-client"] = ["https://foreign.example.com/"];
    });

    if (useFlows) {
        authorization = authorization.UseAuthorizationCodeFlow()
                                     .UseClientCredentialsFlow()
                                     .UseRefreshTokenFlow()
                                     .UseJwtBearerGrant()
                                     .UseResourceIndicators()
                                     .UseIntrospection()
                                     .UsePushedAuthorizationRequests()
                                     .UseNativeSingleSignOn()
                                     .UseClientAssertionAuthentication();
        authorization = builder.Environment.EnvironmentName switch {
            "ReversedRegistration" => authorization.UseRegistrationDelete()
                                                   .UseRegistrationReplace()
                                                   .UseDynamicClientRegistration(),
            "RegistrationReadOnly" => authorization.UseDynamicClientRegistration(),
            _                      => authorization.UseDynamicClientRegistration()
                                                   .UseRegistrationReplace()
                                                   .UseRegistrationDelete(),
        };
        if (builder.Environment.EnvironmentName != "NoClaims") {
            authorization.UseClaimsParameter();
        }
    }

    if (useDeviceFlow) {
        authorization = authorization.UseDeviceFlow();
    }

    if (builder.Environment.EnvironmentName == "Revocation") {
        authorization = authorization.UseRevocation();
    }

    if (useUserInfo) {
        authorization = authorization.UseUserInfo();
    }

    if (builder.Environment.EnvironmentName is "EndSessionOnly" or "Logout") {
        authorization = authorization.UseEndSession();
    }

    if (builder.Environment.EnvironmentName is not ("NoSession" or "AuthenticatedNoSession")) {
        authorization.UseSessionManagement();
    }

    authorization.MapHttp();
    if (builder.Environment.EnvironmentName == "Grpc") authorization.MapGrpc();

    if (builder.Environment.EnvironmentName == "Dpop") {
        authorization.UseDemonstratingProofOfPossession(o => o.RequireForAllClients());
    }

    if (builder.Environment.EnvironmentName == "Native") {
        authorization.UsePairwiseSubjects();
    }

    if (builder.Environment.EnvironmentName == "Jar") {
        authorization.UseJwtSecuredAuthorizationRequests();
    }

    if (builder.Environment.EnvironmentName == "Rar") {
        authorization.UseRichAuthorizationRequests();
        schema.Services.AddSingleton<IAuthorizationDetailTypeDescriptor, Schemata.Authorization.Integration.Tests.PaymentInitiationDescriptor>();
    }

    if (builder.Environment.EnvironmentName is "Authenticated" or "Authorized" or "AuthenticatedNoSession" or "DeviceFlow") {
        authorization.WithAuthentication("ManagementTest");
    }

    if (builder.Environment.EnvironmentName == "Authorized") {
        authorization.WithAuthorization();
    }

    schema.UseAuthentication((AuthenticationBuilder _) => { });
    if (builder.Environment.EnvironmentName == "Logout") {
        schema.Services.AddAuthentication(o => {
            o.DefaultAuthenticateScheme = Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme;
            o.DefaultChallengeScheme = Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme;
        }).AddCookie(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IOpSessionStore, IdentityHostSessionStore>());
    }
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
            Parent    = SecurityParents.Issuer(issuer),
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
            Key       = app.ClientId,
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
        TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
Permissions = ["e:/Connect/Token"],
        GrantTypes = ["client_credentials"],
    };
    await applications.CreateAsync(testApp);
    await SeedPasswordAsync(testApp, "test-secret");

    var dpopApp = new SchemataApplication {
        Name        = "dpop-client",
        ClientId    = "dpop-client",
        TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
Permissions = ["e:/Connect/Token"],
        GrantTypes = ["client_credentials"],
        DpopBoundAccessTokens = true,
    };
    await applications.CreateAsync(dpopApp);
    await SeedPasswordAsync(dpopApp, "dpop-secret");

    var codeApp = new SchemataApplication {
        Name         = "code-client",
        ClientId     = "code-client",
        TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
        RedirectUris = new List<string> { "https://localhost/callback" },
Permissions  = new List<string> { "e:/Connect/Authorize", "e:/Connect/Token" },
        GrantTypes   = ["authorization_code", "refresh_token"],
        ResponseTypes = ["code"],
        Scope        = "openid offline_access",
    };
    await applications.CreateAsync(codeApp);
    await SeedPasswordAsync(codeApp, "code-secret");

    var jwtApp = new SchemataApplication {
        Name        = "jwt-client",
        ClientId    = "jwt-client",
        TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
Permissions = ["e:/Connect/Token"],
        GrantTypes = ["urn:ietf:params:oauth:grant-type:jwt-bearer"],
        Scope = "api:read",
    };
    await applications.CreateAsync(jwtApp);
    await SeedPasswordAsync(jwtApp, "jwt-secret");

    var introspectApp = new SchemataApplication {
        Name        = "introspect-client",
        ClientId    = "introspect-client",
        TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
Permissions = ["e:/Connect/Introspect"],
    };
    await applications.CreateAsync(introspectApp);
    await SeedPasswordAsync(introspectApp, "introspect-secret");

    var foreignIntrospectApp = new SchemataApplication {
        Name        = "foreign-introspect-client",
        ClientId    = "foreign-introspect-client",
        TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
Permissions = ["e:/Connect/Introspect"],
    };
    await applications.CreateAsync(foreignIntrospectApp);
    await SeedPasswordAsync(foreignIntrospectApp, "foreign-introspect-secret");

    var unmappedIntrospectApp = new SchemataApplication {
        Name        = "unmapped-introspect-client",
        ClientId    = "unmapped-introspect-client",
        TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
Permissions = ["e:/Connect/Introspect"],
    };
    await applications.CreateAsync(unmappedIntrospectApp);
    await SeedPasswordAsync(unmappedIntrospectApp, "unmapped-introspect-secret");

    var browserApp = new SchemataApplication {
        Name         = "browser-client",
        ClientId     = "browser-client",
        ApplicationType = ApplicationTypes.Native,
        RedirectUris = new List<string> { "https://localhost/callback" },
        TokenEndpointAuthMethod = ClientAuthMethods.None,
Permissions  = new List<string> { "e:/Connect/Authorize", "e:/Connect/Token" },
        GrantTypes   = ["authorization_code"],
        ResponseTypes = ["code"],
    };
    await applications.CreateAsync(browserApp);

    var deviceApp = new SchemataApplication {
        Name        = "device-client",
        ClientId    = "device-client",
        TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
Permissions = ["e:/Connect/Device", "e:/Connect/Token"],
        GrantTypes = ["urn:ietf:params:oauth:grant-type:device_code"],
        Scope = "api",
    };
    await applications.CreateAsync(deviceApp);
    await SeedPasswordAsync(deviceApp, "device-secret");
}

app.MapGet(
    "/test/whoami",
    [Authorize(Policy = SchemataAuthorizationPolicies.Profile)] (
        HttpContext context
    ) => context.User.Identity?.AuthenticationType ?? string.Empty);

if (builder.Environment.EnvironmentName == "Logout") {
    app.MapPost("/test/login", async (HttpContext http, IOpSessionService sessions) => {
        var form = await http.Request.ReadFormAsync();
        var subject = form["subject"].ToString();
        var sid = form["sid"].ToString();
        var claims = new List<Claim> { new("sub", subject) };
        if (form["sidless"] != "true") claims.Add(new("sid", sid));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme));
        if (form["sidless"] == "true" && form["subject_only"] != "true") {
            var established = new ClaimsPrincipal(new ClaimsIdentity([new("sub", subject), new("sid", sid)], "host"));
            await sessions.IssueAsync(established, subject);
        }
        await http.SignInAsync(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, principal);
        return Results.NoContent();
    });
    app.MapPost("/test/issue", async Task<IResult> (HttpContext http, IApplicationManager<SchemataApplication> applications,
        IAuthorizationSignInService signIn) => {
        if (http.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
        if (await applications.FindByClientIdAsync("logout-smoke") is null) {
            await applications.CreateAsync(new() {
                Name = "logout-smoke", ClientId = "logout-smoke", TokenEndpointAuthMethod = ClientAuthMethods.None,
                Scope = "openid", GrantTypes = [GrantTypes.AuthorizationCode],
                ResponseTypes = [ResponseTypes.Code], PostLogoutRedirectUris = ["https://client.example/done"],
            });
        }
        var principal = new ClaimsPrincipal(http.User.Identities.Select(identity => identity.Clone()));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new(Claims.ClientId, "logout-smoke"));
        var response = await signIn.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode, [Properties.Scope] = "openid",
            [Properties.SessionId] = principal.FindFirstValue(Claims.SessionId),
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Token);
        return Results.Json(response.Token, new System.Text.Json.JsonSerializerOptions {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        });
    });
    app.MapGet("/test/session", (HttpContext http) => Results.Json(new {
        Subject = http.User.FindFirstValue("sub"), SessionId = http.User.FindFirstValue("sid"),
    }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower }));
}

app.Run();

namespace Schemata.Authorization.Integration.Tests
{
    public partial class Program;
}
