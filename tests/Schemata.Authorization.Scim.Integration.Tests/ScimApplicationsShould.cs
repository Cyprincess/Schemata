using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Entities;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Security.Skeleton;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Scim.Integration.Tests;

public class ScimApplicationsShould
{
    private const string ApplicationSchema = "https://github.com/Cyprincess/Schemata/scim/schemas/2.0/Application";
    private const string AuthorizationSchema = "https://github.com/Cyprincess/Schemata/scim/schemas/2.0/ApplicationAuthorization";
    private const string PatchSchema = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public async Task Identity_And_Authorization_Builders_Serve_Their_Collections_Together() {
        using var host = new WebAppFactory("Combined");
        using var client = Client(host);
        using var discovery = JsonDocument.Parse(await client.GetStringAsync("/scim/v2/ResourceTypes"));
        var resources = discovery.RootElement.GetProperty("Resources").EnumerateArray().ToArray();
        foreach (var type in new[] { "User", "Group", "Application" }) {
            Assert.Contains(resources, resource => resource.GetProperty("name").GetString() == type);
        }
        var user = await SendAsync(client, HttpMethod.Post, "/scim/v2/Users", new {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" }, userName = "combined-user",
        });
        Assert.True(user.Status == HttpStatusCode.Created, user.Body);
        var group = await SendAsync(client, HttpMethod.Post, "/scim/v2/Groups", new {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" }, displayName = "combined-group",
        }, privileged: true);
        Assert.True(group.Status == HttpStatusCode.Created, group.Body);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", ValidBody())).Status);
        foreach (var collection in new[] { "Users", "Groups", "Applications" }) {
            var result = await SendAsync(client, HttpMethod.Get, "/scim/v2/" + collection);
            Assert.True(result.Status == HttpStatusCode.OK, result.Body);
            Assert.Single(result.Json().RootElement.GetProperty("Resources").EnumerateArray());
        }
    }
    [Fact]
    public async Task Provisioning_Preserves_Jar_Policy_When_Reader_Feature_Is_Not_Installed() {
        using var host = new WebAppFactory("WithoutJar");
        using var client = Client(host);
        var body = ValidBody();
        body["requestObjectSigningAlg"] = "RS256";
        body["requestObjectEncryptionAlg"] = "RSA-OAEP";
        body["requestObjectEncryptionEnc"] = "A128CBC-HS256";
        body["requireSignedRequestObject"] = true;
        body["requestUris"] = new[] { "https://client.example/request.jwt" };
        var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body);
        Assert.True(created.Status == HttpStatusCode.Created, created.Body);
        var id = created.Json().RootElement.GetProperty("id").GetString();
        var read = await SendAsync(client, HttpMethod.Get, "/scim/v2/Applications/" + id);
        Assert.Equal("RS256", read.Json().RootElement.GetProperty("requestObjectSigningAlg").GetString());
        Assert.True(read.Json().RootElement.GetProperty("requireSignedRequestObject").GetBoolean());
        using var scope = host.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        var stored = await context.Applications.AsNoTracking().SingleAsync();
        Assert.Equal("RS256", stored.RequestObjectSigningAlg);
        Assert.Equal("RSA-OAEP", stored.RequestObjectEncryptionAlg);
        Assert.Equal("A128CBC-HS256", stored.RequestObjectEncryptionEnc);
        Assert.True(stored.RequireSignedRequestObject);
        Assert.Null(scope.ServiceProvider.GetService<RequestObjectReader<SchemataApplication, Schemata.Security.Skeleton.Entities.SchemataSecurity>>());
    }


    [Fact]
    public async Task Dcr_Key_Restrictions_Survive_Scim_Get_And_Put() {
        using var host = new WebAppFactory();
        using var client = Client(host);
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "initial-token");
        using var registered = await client.PostAsJsonAsync("/connect/register", new {
            grant_types = new[] { GrantTypes.AuthorizationCode }, response_types = new[] { ResponseTypes.Code },
            redirect_uris = new[] { "https://localhost/callback" }, token_endpoint_auth_method = ClientAuthMethods.None,
            id_token_encrypted_response_alg = "RSA-OAEP",
            jwks = new { keys = new[] { new { kty = "RSA", kid = "restricted", use = "enc", alg = "RSA-OAEP",
                n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!), key_ops = new[] { "verify" } } } },
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        string id;
        using (var scope = host.Services.CreateScope()) {
            await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
            id = (await context.Applications.SingleAsync()).Uid.ToString();
        }
        var get = await SendAsync(client, HttpMethod.Get, "/scim/v2/Applications/" + id);
        Assert.Equal("verify", get.Json().RootElement.GetProperty("jwks")[0].GetProperty("key_ops")[0].GetString());
        var put = await SendAsync(client, HttpMethod.Put, "/scim/v2/Applications/" + id, get.Json().RootElement);
        Assert.True(put.Status == HttpStatusCode.OK, put.Body);
        using var fresh = host.Services.CreateScope();
        await using var freshContext = await fresh.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        var application = await freshContext.Applications.AsNoTracking().SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.ServiceProvider.GetRequiredService<IIdTokenResponseProtector>()
            .ProtectAsync(application, "signed.token.value"));
    }

    [Theory]
    [InlineData("requestObjectSigningAlg", "unsupported")]
    [InlineData("requestObjectEncryptionAlg", "unsupported")]
    [InlineData("requestUris", "http://client.example/request.jwt")]
    public async Task Invalid_Security_Metadata_Is_Rejected_Without_Persistence(string name, string value) {
        using var host = new WebAppFactory();
        using var client = Client(host);
        var body = ValidBody();
        body[name] = name == "requestUris" ? new[] { value } : value;
        var response = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("invalidValue", response.Json().RootElement.GetProperty("scimType").GetString());
        using var scope = host.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        Assert.Empty(context.Applications);
    }

    [Fact]
    public async Task Scim_Security_Metadata_Survives_Roundtrip_And_Controls_Encryption_And_Jar() {
        using var host = new WebAppFactory();
        using var client = Client(host);
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var body = ValidBody();
        body["idTokenEncryptedResponseAlg"] = "RSA-OAEP";
        body["idTokenEncryptedResponseEnc"] = "A128CBC-HS256";
        body["requestObjectSigningAlg"] = "RS256";
        body["requestObjectEncryptionAlg"] = "RSA-OAEP";
        body["requestObjectEncryptionEnc"] = "A128CBC-HS256";
        body["requireSignedRequestObject"] = true;
        body["requestUris"] = new[] { "https://client.example/request.jwt" };
        body["jwks"] = new[] { new {
            kty = "RSA", kid = "encryption", use = "enc", alg = "RSA-OAEP",
            n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!),
            key_ops = new[] { "encrypt" },
        }, new {
            kty = "RSA", kid = "signing", use = "sig", alg = "RS256",
            n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!),
            key_ops = new[] { "verify" },
        } };
        var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body);
        Assert.True(created.Status == HttpStatusCode.Created, created.Body);
        var id = created.Json().RootElement.GetProperty("id").GetString();
        var get = await SendAsync(client, HttpMethod.Get, "/scim/v2/Applications/" + id);
        var replaced = await SendAsync(client, HttpMethod.Put, "/scim/v2/Applications/" + id, get.Json().RootElement);
        Assert.True(replaced.Status == HttpStatusCode.OK, replaced.Body);
        Assert.Equal("encrypt", replaced.Json().RootElement.GetProperty("jwks")[0].GetProperty("key_ops")[0].GetString());
        using var scope = host.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        var stored = await context.Applications.AsNoTracking().SingleAsync();
        Assert.Equal("RSA-OAEP", stored.IdTokenEncryptedResponseAlg);
        Assert.Equal("A128CBC-HS256", stored.IdTokenEncryptedResponseEnc);
        Assert.Equal("RS256", stored.RequestObjectSigningAlg);
        Assert.Equal("RSA-OAEP", stored.RequestObjectEncryptionAlg);
        Assert.True(stored.RequireSignedRequestObject);
        Assert.Equal(new[] { "https://client.example/request.jwt" }, stored.RequestUris);
        var protector = scope.ServiceProvider.GetRequiredService<IIdTokenResponseProtector>();
        var signed = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Claims = new Dictionary<string, object> { ["sub"] = "user" },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), "RS256"),
        });
        Assert.Equal(5, (await protector.ProtectAsync(stored, signed)).Split('.').Length);
        var jar = scope.ServiceProvider.GetRequiredService<RequestObjectReader<SchemataApplication, Schemata.Security.Skeleton.Entities.SchemataSecurity>>();
        var jarClaims = new Dictionary<string, object> {
            ["client_id"] = stored.ClientId!, ["redirect_uri"] = "https://localhost/callback", ["response_type"] = "code",
            ["scope"] = "openid offline_access", ["state"] = "verified-object",
        };
        var jarDescriptor = new SecurityTokenDescriptor {
            Issuer = stored.ClientId, Audience = "https://localhost", Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = jarClaims, SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "signing" }, "RS256"),
        };
        var target = new AuthorizeRequest { ClientId = stored.ClientId, Scope = "openid offline_access", ResponseType = "code" };
        await jar.ReadAsync(new JsonWebTokenHandler().CreateToken(jarDescriptor), stored, target, true, default);
        Assert.Equal("verified-object", target.State);
        jarDescriptor.SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "signing" }, "RS384");
        var jarError = await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.OAuthException>(() => jar.ReadAsync(
            new JsonWebTokenHandler().CreateToken(jarDescriptor), stored,
            new AuthorizeRequest { ClientId = stored.ClientId, Scope = "openid offline_access", ResponseType = "code" }, true, default));
        Assert.Equal(OAuthErrors.InvalidRequestObject, jarError.Status);
        var restrict = JsonSerializer.Deserialize<Dictionary<string, object?>>(get.Body)!;
        restrict["jwks"] = new[] { new {
            kty = "RSA", kid = "encryption", use = "enc", alg = "RSA-OAEP",
            n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!), key_ops = new[] { "verify" },
        } };
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, "/scim/v2/Applications/" + id, restrict)).Status);
        using var fresh = host.Services.CreateScope();
        await using var freshContext = await fresh.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        var restricted = await freshContext.Applications.AsNoTracking().SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.ServiceProvider.GetRequiredService<IIdTokenResponseProtector>().ProtectAsync(restricted, signed));
    }

    [Fact]
    public async Task Discovery_Advertises_The_Exact_Application_Schemas_And_Public_Key_Shape() {
        using var host = new WebAppFactory();
        using var client = Client(host);

        using var resourceTypes = JsonDocument.Parse(await client.GetStringAsync("/scim/v2/ResourceTypes"));
        var application = resourceTypes.RootElement.GetProperty("Resources").EnumerateArray()
                                      .Single(type => type.GetProperty("name").GetString() == "Application");
        Assert.Equal(ApplicationSchema, application.GetProperty("schema").GetString());
        Assert.Equal("/Applications", application.GetProperty("endpoint").GetString());
        Assert.Equal(AuthorizationSchema, application.GetProperty("schemaExtensions")[0].GetProperty("schema").GetString());

        using var schemas = JsonDocument.Parse(await client.GetStringAsync("/scim/v2/Schemas"));
        var schema = schemas.RootElement.GetProperty("Resources").EnumerateArray()
                            .Single(candidate => candidate.GetProperty("id").GetString() == ApplicationSchema);
        var jwks = schema.GetProperty("attributes").EnumerateArray().Single(attribute => attribute.GetProperty("name").GetString() == "jwks");
        Assert.True(jwks.GetProperty("multiValued").GetBoolean());
        Assert.Equal("complex", jwks.GetProperty("type").GetString());
        Assert.DoesNotContain(jwks.GetProperty("subAttributes").EnumerateArray(), attribute => attribute.GetProperty("name").GetString() == "keys");
        var names = schema.GetProperty("attributes").EnumerateArray().Select(attribute => attribute.GetProperty("name").GetString()).ToArray();
        Assert.DoesNotContain("clientSecret", names);
        Assert.DoesNotContain("registrationAccessToken", names);
        Assert.DoesNotContain("tokens", names);
    }

    [Fact]
    public async Task Application_Lifecycle_Preserves_Identity_Public_Metadata_And_Credential_Boundaries() {
        using var host = new WebAppFactory();
        using var client = Client(host);
        var body = ValidBody();
        body["externalId"] = "external-1";
        body["jwks"] = PublicKeys("rsa-1");

        var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body);
        Assert.True(HttpStatusCode.Created == created.Status, created.Body);
        using var createdJson = JsonDocument.Parse(created.Body);
        var id = createdJson.RootElement.GetProperty("id").GetString();
        Assert.True(Guid.TryParse(id, out var uid));
        var clientId = createdJson.RootElement.TryGetProperty("clientId", out var clientIdElement) ? clientIdElement.GetString() : null;
        Assert.False(string.IsNullOrWhiteSpace(clientId), created.Body);
        Assert.NotEqual(id, clientId);
        Assert.Equal("external-1", createdJson.RootElement.GetProperty("externalId").GetString());
        Assert.Equal(2, createdJson.RootElement.GetProperty("jwks").GetArrayLength());
        AssertRedacted(createdJson.RootElement);

        using (var scope = host.Services.CreateScope())
        await using (var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync()) {
            var application = await context.Applications.SingleAsync();
            Assert.Equal(uid, application.Uid);
            Assert.Equal(clientId, application.ClientId);
            Assert.Equal([GrantTypes.AuthorizationCode, GrantTypes.RefreshToken], application.GrantTypes);
            Assert.Equal([ResponseTypes.Code], application.ResponseTypes);
            Assert.Equal("openid offline_access", application.Scope);
            Assert.Null(application.Permissions);
            Assert.False(string.IsNullOrWhiteSpace(application.Name));
            var binding = await context.ProvisioningBindings.SingleAsync();
            Assert.Equal("Application", binding.ResourceType);
            Assert.Equal(uid, binding.EntityUid);
            Assert.Equal("domain-a", binding.ProvisioningDomain);
            Assert.Equal("external-1", binding.ExternalId);
            Assert.Equal(1, await context.Securities.CountAsync(row => row.Parent == application.CanonicalName && row.Kind == SecurityConstants.Kinds.Jwks));
            Assert.Empty(context.Tokens);
        }

        var read = await SendAsync(client, HttpMethod.Get, $"/scim/v2/Applications/{id}");
        Assert.Equal(HttpStatusCode.OK, read.Status);
        AssertRedacted(JsonDocument.Parse(read.Body).RootElement);
        var list = await SendAsync(client, HttpMethod.Get, "/scim/v2/Applications");
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.Contains(id, list.Body, StringComparison.Ordinal);

        var replacement = ValidBody();
        replacement["clientId"] = "forged-client-id";
        replacement["clientName"] = "Replaced";
        replacement["externalId"] = "external-2";
        var replaced = await SendAsync(client, HttpMethod.Put, $"/scim/v2/Applications/{id}", replacement);
        Assert.True(HttpStatusCode.OK == replaced.Status, replaced.Body);
        using var replacedJson = JsonDocument.Parse(replaced.Body);
        Assert.Equal(id, replacedJson.RootElement.GetProperty("id").GetString());
        Assert.Equal(clientId, replacedJson.RootElement.GetProperty("clientId").GetString());
        Assert.Equal("Replaced", replacedJson.RootElement.GetProperty("clientName").GetString());

        var patched = await SendAsync(client, HttpMethod.Patch, $"/scim/v2/Applications/{id}", new {
            schemas = new[] { PatchSchema },
            Operations = new[] { new { op = "replace", path = "clientName", value = "Patched" } },
        });
        Assert.Equal(HttpStatusCode.OK, patched.Status);
        using var patchedJson = JsonDocument.Parse(patched.Body);
        Assert.Equal(clientId, patchedJson.RootElement.GetProperty("clientId").GetString());
        Assert.Equal("Patched", patchedJson.RootElement.GetProperty("clientName").GetString());

        var deleted = await SendAsync(client, HttpMethod.Delete, $"/scim/v2/Applications/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/scim/v2/Applications/{id}")).Status);
        using var verifyScope = host.Services.CreateScope();
        await using var verify = await verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        Assert.Empty(verify.Applications);
        Assert.Empty(verify.ProvisioningBindings);
        Assert.Empty(verify.Securities.Where(row => row.Parent != "https://localhost"));
    }

    [Fact]
    public async Task Permission_Changes_Require_Delegation_And_Omitted_Policy_Is_Preserved() {
        using var host = new WebAppFactory();
        using var client = Client(host);
        var body = ValidBody();
        body["externalId"] = "policy-target";
        body[AuthorizationSchema] = new { permissions = new[] { "e:/Connect/Authorize", "e:/Connect/Token" } };
        var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body, privileged: true);
        Assert.True(HttpStatusCode.Created == created.Status, created.Body);
        using var createdJson = JsonDocument.Parse(created.Body);
        var id = createdJson.RootElement.GetProperty("id").GetString();
        var clientId = createdJson.RootElement.GetProperty("clientId").GetString();

        var ordinaryPut = ValidBody();
        ordinaryPut["clientId"] = "forged-client-id";
        ordinaryPut["clientName"] = "Ordinary replace";
        var preserved = await SendAsync(client, HttpMethod.Put, $"/scim/v2/Applications/{id}", ordinaryPut);
        Assert.Equal(HttpStatusCode.OK, preserved.Status);
        using var preservedJson = JsonDocument.Parse(preserved.Body);
        Assert.Equal(clientId, preservedJson.RootElement.GetProperty("clientId").GetString());
        Assert.Equal(2, preservedJson.RootElement.GetProperty(AuthorizationSchema).GetProperty("permissions").GetArrayLength());

        var escalation = ValidBody();
        escalation["clientName"] = "Escalation";
        escalation[AuthorizationSchema] = new { permissions = new[] { "e:/Connect/Introspect" } };
        var denied = await SendAsync(client, HttpMethod.Put, $"/scim/v2/Applications/{id}", escalation);
        Assert.Equal(HttpStatusCode.Forbidden, denied.Status);
        Assert.Equal("Ordinary replace", (await SendAsync(client, HttpMethod.Get, $"/scim/v2/Applications/{id}")).Json().RootElement.GetProperty("clientName").GetString());

        var delegated = await SendAsync(client, HttpMethod.Put, $"/scim/v2/Applications/{id}", escalation, privileged: true);
        Assert.Equal(HttpStatusCode.OK, delegated.Status);
        Assert.Equal("e:/Connect/Introspect", delegated.Json().RootElement.GetProperty(AuthorizationSchema).GetProperty("permissions")[0].GetString());
    }

    [Fact]
    public async Task External_Ids_Are_Scoped_To_Their_Provisioning_Domain() {
        using var host = new WebAppFactory();
        using var client = Client(host);
        var first = ValidBody();
        first["externalId"] = "shared-external";
        var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", first);
        Assert.True(HttpStatusCode.Created == created.Status, created.Body);
        var id = created.Json().RootElement.GetProperty("id").GetString();

        var duplicate = ValidBody();
        duplicate["externalId"] = "shared-external";
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", duplicate)).Status);

        var otherDomain = ValidBody();
        otherDomain["externalId"] = "shared-external";
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", otherDomain, domain: "domain-b")).Status);

        var crossDomain = ValidBody();
        crossDomain["clientName"] = "Cross-domain write";
        var denied = await SendAsync(client, HttpMethod.Put, $"/scim/v2/Applications/{id}", crossDomain, domain: "domain-b");
        Assert.Equal(HttpStatusCode.Forbidden, denied.Status);
        Assert.Equal("Example Application", (await SendAsync(client, HttpMethod.Get, $"/scim/v2/Applications/{id}")).Json().RootElement.GetProperty("clientName").GetString());
    }

    [Theory]
    [InlineData("""[{"kty":"RSA","kid":"private","n":"x","e":"AQAB","d":"private-value"}]""")]
    [InlineData("""[{"kty":"oct","kid":"symmetric","k":"secret-value"}]""")]
    public async Task Private_Or_Symmetric_Jwks_Is_Rejected_Without_Persistence(string keys) {
        using var host = new WebAppFactory();
        using var client = Client(host);
        var body = ValidBody();
        body["jwks"] = JsonSerializer.Deserialize<JsonElement>(keys);

        var response = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("invalidValue", response.Json().RootElement.GetProperty("scimType").GetString());
        Assert.DoesNotContain("private-value", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", response.Body, StringComparison.Ordinal);
        using var scope = host.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        Assert.Empty(context.Applications);
        Assert.Empty(context.Securities.Where(row => row.Kind == SecurityConstants.Kinds.Jwks));
    }

    [Fact]
    public async Task Binding_Failure_Rolls_Back_Create_Metadata_And_Security_Rows() {
        using var host = new WebAppFactory();
        using var client = Client(host);
        var body = ValidBody();
        body["externalId"] = "fail-binding";
        body["jwks"] = PublicKeys("create-rollback");

        var response = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        using var scope = host.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        Assert.Empty(context.Applications);
        Assert.Empty(context.ProvisioningBindings);
        Assert.Empty(context.Securities.Where(row => row.Kind == SecurityConstants.Kinds.Jwks));
    }

    [Theory]
    [InlineData("security")]
    [InlineData("binding")]
    public async Task Mid_Patch_Failure_Restores_Application_Security_And_Binding(string failure) {
        using var host = new WebAppFactory();
        using var client = Client(host);
        var body = ValidBody();
        body["externalId"] = "before-external";
        body["jwks"] = PublicKeys("before-key");
        var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body);
        Assert.True(HttpStatusCode.Created == created.Status, created.Body);
        var id = created.Json().RootElement.GetProperty("id").GetString();
        var before = await SnapshotAsync(host);

        var keys = failure == "security" ? PublicKeys("fail-security") : PublicKeys("after-key");
        var externalId = failure == "binding" ? "fail-binding" : "after-external";
        var response = await SendAsync(client, HttpMethod.Patch, $"/scim/v2/Applications/{id}", new {
            schemas = new[] { PatchSchema },
            Operations = new object[] {
                new { op = "replace", path = "clientName", value = "After" },
                new { op = "replace", path = "externalId", value = externalId },
                new { op = "replace", path = "jwks", value = keys },
            },
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Equal(before, await SnapshotAsync(host));
    }

    [Fact]
    public async Task Standard_Metadata_Drives_OAuth_And_Delete_Cascades_Runtime_State() {
        using var host = new WebAppFactory();
        using var client = Client(host);
        var body = ValidBody();
        body["externalId"] = "oauth-client";
        using var encryptionKey = RSA.Create(2048);
        var encryptionParameters = encryptionKey.ExportParameters(false);
        body["idTokenEncryptedResponseAlg"] = "RSA-OAEP";
        body["idTokenEncryptedResponseEnc"] = "A128CBC-HS256";
        body["jwks"] = new[] { new {
            kty = "RSA", kid = "response-key", use = "enc", alg = "RSA-OAEP", key_ops = new[] { "encrypt" },
            n = Base64UrlEncoder.Encode(encryptionParameters.Modulus!), e = Base64UrlEncoder.Encode(encryptionParameters.Exponent!),
        } };
        body[AuthorizationSchema] = new { permissions = new[] { "e:/Connect/Authorize", "e:/Connect/Token" } };
        var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", body, privileged: true);
        Assert.True(HttpStatusCode.Created == created.Status, created.Body);
        var root = created.Json().RootElement;
        var id = root.GetProperty("id").GetString();
        var clientId = root.GetProperty("clientId").GetString();

        var authorize = await client.GetAsync("/connect/authorize?client_id=" + clientId
            + "&redirect_uri=https%3A%2F%2Flocalhost%2Fcallback&response_type=code&scope=openid%20offline_access"
            + "&code_challenge=" + Challenge + "&code_challenge_method=S256");
        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);
        var interaction = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query);
        var approve = await client.PostAsync("/connect/interact", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["code"] = interaction[Parameters.Code]!,
            ["code_type"] = interaction["code_type"]!,
        }));
        Assert.Equal(HttpStatusCode.Found, approve.StatusCode);
        var callback = HttpUtility.ParseQueryString(approve.Headers.Location!.Query);
        var code = callback[Parameters.Code];
        Assert.False(string.IsNullOrWhiteSpace(code));

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = GrantTypes.AuthorizationCode,
            ["client_id"] = clientId!,
            ["code"] = code!,
            ["redirect_uri"] = "https://localhost/callback",
            ["code_verifier"] = Verifier,
        }));
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        using var tokenJson = JsonDocument.Parse(await token.Content.ReadAsStringAsync());
        var refresh = tokenJson.RootElement.GetProperty("refresh_token").GetString();
        var encrypted = new JsonWebToken(tokenJson.RootElement.GetProperty("id_token").GetString()!);
        Assert.Equal("JWT", encrypted.Cty);
        Assert.Equal("RSA-OAEP", encrypted.Alg);
        var inner = new JsonWebTokenHandler().DecryptToken(encrypted, new TokenValidationParameters {
            TokenDecryptionKey = new RsaSecurityKey(encryptionKey),
        });
        using (var verification = host.Services.CreateScope()) {
            var claims = await verification.ServiceProvider.GetRequiredService<TokenService<Schemata.Security.Skeleton.Entities.SchemataSecurity>>().Validate(inner);
            Assert.NotNull(claims);
            Assert.Equal(clientId, claims.FindFirst("aud")?.Value);
            Assert.Equal("https://localhost", claims.FindFirst("iss")?.Value);
            Assert.Equal("users/u-1", claims.FindFirst("sub")?.Value);
            Assert.NotNull(claims.FindFirst("exp"));
        }
        Assert.False(string.IsNullOrWhiteSpace(refresh));

        var patched = await SendAsync(client, HttpMethod.Patch, $"/scim/v2/Applications/{id}", new {
            schemas = new[] { PatchSchema },
            Operations = new object[] {
                new { op = "replace", path = "grantTypes", value = new[] { GrantTypes.AuthorizationCode } },
                new { op = "replace", path = "scope", value = "openid" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, patched.Status);
        var refreshed = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = GrantTypes.RefreshToken,
            ["client_id"] = clientId!,
            ["refresh_token"] = refresh!,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, refreshed.StatusCode);
        using var rejected = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        Assert.Equal(OAuthErrors.UnauthorizedClient, rejected.RootElement.GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/scim/v2/Applications/{id}")).Status);
        using var scope = host.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        Assert.Empty(context.Applications);
        Assert.Empty(context.ProvisioningBindings);
        Assert.DoesNotContain(context.Tokens, token => token.Application is not null || token.Authorization is not null);
        Assert.Empty(context.Securities.Where(row => row.Parent != "https://localhost"));
    }

    [Fact]
    public async Task Provisioning_Write_Denial_Does_Not_Create_An_Application() {
        using var host = new WebAppFactory("Denied");
        using var client = Client(host);

        var response = await SendAsync(client, HttpMethod.Post, "/scim/v2/Applications", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        using var scope = host.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        Assert.Empty(context.Applications);
    }

    private static Dictionary<string, object?> ValidBody() {
        return new() {
            ["schemas"] = new[] { ApplicationSchema },
            ["redirectUris"] = new[] { "https://localhost/callback" },
            ["tokenEndpointAuthMethod"] = ClientAuthMethods.None,
            ["grantTypes"] = new[] { GrantTypes.AuthorizationCode, GrantTypes.RefreshToken },
            ["responseTypes"] = new[] { ResponseTypes.Code },
            ["scope"] = "openid offline_access",
            ["clientName"] = "Example Application",
            ["initiateLoginUri"] = "https://localhost/login",
        };
    }

    private static JsonElement PublicKeys(string kid) {
        return JsonSerializer.Deserialize<JsonElement>($$"""
            [
              {"kty":"RSA","kid":"{{kid}}","use":"sig","alg":"RS256","n":"sample-modulus","e":"AQAB"},
              {"kty":"EC","kid":"{{kid}}-ec","use":"sig","alg":"ES256","crv":"P-256","x":"sample-x","y":"sample-y"}
            ]
            """);
    }

    private static HttpClient Client(WebAppFactory host) {
        var client = host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Scim", "1");
        return client;
    }

    private static async Task<Response> SendAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        object? body = null,
        bool privileged = false,
        string? domain = null
    ) {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null) {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/scim+json");
        }
        if (privileged) {
            request.Headers.Add("X-Test-Permissions", "allow");
        }
        if (domain is not null) {
            request.Headers.Add("X-Test-Domain", domain);
        }
        using var response = await client.SendAsync(request);
        return new(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> SnapshotAsync(WebAppFactory host) {
        using var scope = host.Services.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationScimDbContext>>().CreateDbContextAsync();
        var application = await context.Applications.AsNoTracking().SingleAsync();
        var binding = await context.ProvisioningBindings.AsNoTracking().SingleAsync();
        var security = await context.Securities.AsNoTracking()
                                    .Where(row => row.Parent == application.CanonicalName)
                                    .OrderBy(row => row.Kind).ThenBy(row => row.CreateTime)
                                    .Select(row => $"{row.Kind}|{row.Status}|{row.Value}")
                                    .ToArrayAsync();
        return $"{application.Uid}|{application.ClientId}|{application.ClientName}|{binding.ExternalId}|{string.Join(";", security)}";
    }

    private static void AssertRedacted(JsonElement root) {
        foreach (var property in root.EnumerateObject()) {
            Assert.DoesNotContain(property.Name, new[] { "clientSecret", "clientSecretExpiresAt", "registrationAccessToken", "registrationClientUri", "accessToken", "refreshToken", "password" });
            if (property.Name == "jwks") {
                foreach (var key in property.Value.EnumerateArray()) {
                    Assert.False(key.TryGetProperty("d", out _));
                    Assert.False(key.TryGetProperty("k", out _));
                }
            }
        }
    }

    private sealed record Response(HttpStatusCode Status, string Body)
    {
        public JsonDocument Json() => JsonDocument.Parse(Body);
    }

    private sealed class WebAppFactory(string environment = "Testing") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment(environment);
            builder.UseContentRoot(Path.GetDirectoryName(typeof(WebAppFactory).Assembly.Location)!);
        }
    }
}
