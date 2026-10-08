using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Messaging.Skeleton;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     RFC 7592 §2.2 replace over real HTTP: a PUT shares the create path's effective policy —
///     trusted software statements, registration advisors, and protocol-shaped errors — while a
///     replace never merges: omitted writable metadata clears instead of retaining old values.
/// </summary>
public class RegisterReplaceFlowShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Return_405_For_Disabled_Management_Methods_While_Read_Remains(string method) {
        using var host = factory.WithEnvironment("RegistrationReadOnly");
        using var client = host.CreateClient();
        var (clientId, rat, _) = await RegisterAsync(client, new() { ["redirect_uris"] = new[] { "https://rp.example/cb" } });
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/connect/register/{clientId}") {
            Content = JsonContent.Create(new { client_id = clientId }),
        };
        request.Headers.Authorization = new("Bearer", rat);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        using var read = new HttpRequestMessage(HttpMethod.Get, $"/connect/register/{clientId}");
        read.Headers.Authorization = new("Bearer", rat);
        using var readable = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Fail_Local_Dispatch_When_The_Write_Features_Are_Not_Installed() {
        using var host = factory.WithEnvironment("RegistrationReadOnly");
        using var client = host.CreateClient();
        var (clientId, rat, _) = await RegisterAsync(client, new() { ["redirect_uris"] = new[] { "https://rp.example/cb" } });

        using var scope = host.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync<RegisterReplaceQuery, RegistrationResponse?>(new(clientId, new(), rat)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync<RegisterDeleteQuery, bool>(new(clientId, rat)));
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Serve_The_Full_Management_Surface_Under_A_Reversed_Feature_Order() {
        using var host = factory.WithEnvironment("ReversedRegistration");
        using var client = host.CreateClient();
        var (clientId, rat, _) = await RegisterAsync(client, new() { ["redirect_uris"] = new[] { "https://rp.example/cb" } });

        var replaced = await ReplaceAsync(client, clientId, new() {
            ["client_id"] = clientId, ["redirect_uris"] = new[] { "https://rp.example/cb" }, ["client_name"] = "Reversed",
        }, rat);
        Assert.Equal(HttpStatusCode.OK, replaced.Status);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/connect/register/{clientId}");
        delete.Headers.Authorization = new("Bearer", rat);
        using var deleted = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var read = new HttpRequestMessage(HttpMethod.Get, $"/connect/register/{clientId}");
        read.Headers.Authorization = new("Bearer", rat);
        using var gone = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.Unauthorized, gone.StatusCode);
    }

    [Fact]
    public async Task Accept_Only_Installed_Rich_Types_And_Clear_Them_On_Replace() {
        using var host = factory.WithEnvironment("Rar");
        using var client = host.CreateClient();
        var rejected = await PostRawAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
            ["authorization_details_types"] = new[] { "unknown" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.Status);
        using var error = JsonDocument.Parse(rejected.Body);
        Assert.Equal(OAuthErrors.InvalidClientMetadata, error.RootElement.GetProperty("error").GetString());
        var (clientId, rat, created) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
            ["authorization_details_types"] = new[] { "payment_initiation" },
        });
        Assert.Equal("payment_initiation", created.GetProperty("authorization_details_types")[0].GetString());
        var replaced = await ReplaceAsync(client, clientId, new() {
            ["client_id"] = clientId, ["redirect_uris"] = new[] { "https://rp.example/cb" },
        }, rat);
        Assert.Equal(HttpStatusCode.OK, replaced.Status);
        using var cleared = JsonDocument.Parse(replaced.Body);
        Assert.False(cleared.RootElement.TryGetProperty("authorization_details_types", out _));
    }

    [Fact]
    public async Task Replace_Echoes_New_Metadata_And_Clears_Omitted_Values() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        var (clientId, rat, created) = await RegisterAsync(client, new() {
            ["redirect_uris"]                         = new[] { "https://rp.example/cb" },
            ["client_name"]                           = "Before",
            ["require_pushed_authorization_requests"] = true,
        });

        Assert.True(created.GetProperty("require_pushed_authorization_requests").GetBoolean());

        var (status, body, _) = await ReplaceAsync(client, clientId, new() {
            ["client_id"]     = clientId,
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
            ["client_name"]   = "After",
        }, rat);

        Assert.Equal(HttpStatusCode.OK, status);
        var replaced = JsonDocument.Parse(body).RootElement;
        Assert.Equal("After", replaced.GetProperty("client_name").GetString());
        Assert.True(!replaced.TryGetProperty("require_pushed_authorization_requests", out var par)
                 || par.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Roundtrip_And_Clear_Localized_And_Dpop_Metadata_Through_Persistence() {
        using var host = factory.WithEnvironment("Dpop");
        using var client = host.CreateClient();
        var (clientId, rat, created) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
            ["client_name#fr-CA"] = "Nom",
            ["logo_uri#fr-CA"] = "https://rp.example/fr.png",
            ["dpop_bound_access_tokens"] = true,
        });
        Assert.Equal("Nom", created.GetProperty("client_name#fr-CA").GetString());
        using var read = new HttpRequestMessage(HttpMethod.Get, $"/connect/register/{clientId}");
        read.Headers.Authorization = new("Bearer", rat);
        using var response = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var stored = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Nom", stored.RootElement.GetProperty("client_name#fr-CA").GetString());
        Assert.Equal("https://rp.example/fr.png", stored.RootElement.GetProperty("logo_uri#fr-CA").GetString());
        Assert.True(stored.RootElement.GetProperty("dpop_bound_access_tokens").GetBoolean());
        Assert.Equal(created.GetProperty("client_id_issued_at").GetInt64(), stored.RootElement.GetProperty("client_id_issued_at").GetInt64());
        var replaced = await ReplaceAsync(client, clientId, new() {
            ["client_id"] = clientId, ["redirect_uris"] = new[] { "https://rp.example/cb" },
        }, stored.RootElement.GetProperty("registration_access_token").GetString());
        Assert.Equal(HttpStatusCode.OK, replaced.Status);
        using var cleared = JsonDocument.Parse(replaced.Body);
        Assert.False(cleared.RootElement.TryGetProperty("client_name#fr-CA", out _));
        Assert.False(cleared.RootElement.TryGetProperty("logo_uri#fr-CA", out _));
        Assert.False(cleared.RootElement.GetProperty("dpop_bound_access_tokens").GetBoolean());
    }

    [Fact]
    public async Task Replace_Requires_A_Registration_Access_Token_Bound_To_The_Client() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        var (clientId, _, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        });
        var (_, otherRat, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://other.example/cb" },
        });

        var anonymous = await ReplaceAsync(client, clientId, new() {
            ["client_id"]     = clientId,
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        }, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.Status);
        Assert.Equal("Bearer error=\"invalid_token\"", anonymous.WwwAuthenticate);

        var foreign = await ReplaceAsync(client, clientId, new() {
            ["client_id"]     = clientId,
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        }, otherRat);
        Assert.Equal(HttpStatusCode.Unauthorized, foreign.Status);
        Assert.Equal("Bearer error=\"invalid_token\"", foreign.WwwAuthenticate);
    }

    [Fact]
    public async Task Replace_Rejects_Server_Managed_Fields_With_Invalid_Request() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        var (clientId, rat, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        });

        var (status, body, _) = await ReplaceAsync(client, clientId, new() {
            ["client_id"]               = clientId,
            ["redirect_uris"]           = new[] { "https://rp.example/cb" },
            ["registration_client_uri"] = "https://forged.example/register",
        }, rat);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(OAuthErrors.InvalidRequest, JsonDocument.Parse(body).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Registration_Maps_Malformed_Bodies_To_Protocol_Errors() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        // Malformed JSON and wrongly-typed fields are OAuth errors, never uncaught 500s.
        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/connect/register") {
            Content = new StringContent("{ not json", Encoding.UTF8, "application/json"),
        };
        malformed.Headers.Authorization = new("Bearer", "initial-token");
        using var malformedResponse = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        var malformedBody = JsonDocument.Parse(await malformedResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidClientMetadata, malformedBody.GetProperty("error").GetString());

        var (wrongTypeStatus, wrongTypeBody) = await PostRawAsync(client, new() {
            ["redirect_uris"] = "not-an-array",
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongTypeStatus);
        Assert.Equal(OAuthErrors.InvalidClientMetadata,
            JsonDocument.Parse(wrongTypeBody).RootElement.GetProperty("error").GetString());

        var (statementStatus, statementBody) = await PostRawAsync(client, new() {
            ["redirect_uris"]      = new[] { "https://rp.example/cb" },
            ["software_statement"] = 42,
        });
        Assert.Equal(HttpStatusCode.BadRequest, statementStatus);
        Assert.Equal(OAuthErrors.InvalidSoftwareStatement,
            JsonDocument.Parse(statementBody).RootElement.GetProperty("error").GetString());

        var (clientId, rat, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        });
        var (putStatus, putBody, _) = await ReplaceAsync(client, clientId, new() {
            ["client_id"]     = clientId,
            ["redirect_uris"] = 42,
        }, rat);
        Assert.Equal(HttpStatusCode.BadRequest, putStatus);
        Assert.Equal(OAuthErrors.InvalidClientMetadata,
            JsonDocument.Parse(putBody).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Trusted_Statements_Take_Priority_And_Echo_Exactly_On_Create_And_Replace() {
        using var host = factory.WithEnvironment("Testing")
                                .WithServices(services => services.AddSingleton<ISoftwareStatementValidator>(new TrustingValidator()));
        using var client = host.CreateClient();

        var (clientId, rat, created) = await RegisterAsync(client, new() {
            ["redirect_uris"]      = new[] { "https://rp.example/cb" },
            ["client_name"]        = "Body Name",
            ["software_statement"] = TrustingValidator.Statement,
        });

        // The statement claim wins over the body; the server-managed client_id claim is ignored;
        // the submitted statement string is stored and echoed verbatim.
        Assert.Equal("Trusted RP", created.GetProperty("client_name").GetString());
        Assert.Equal(clientId, created.GetProperty("client_id").GetString());
        Assert.Equal(TrustingValidator.Statement, created.GetProperty("software_statement").GetString());

        var (status, body, _) = await ReplaceAsync(client, clientId, new() {
            ["client_id"]          = clientId,
            ["redirect_uris"]      = new[] { "https://rp.example/cb" },
            ["client_name"]        = "Replaced Body",
            ["software_statement"] = TrustingValidator.Statement,
        }, rat);

        Assert.Equal(HttpStatusCode.OK, status);
        var replaced = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Trusted RP", replaced.GetProperty("client_name").GetString());
        Assert.Equal(clientId, replaced.GetProperty("client_id").GetString());
        Assert.Equal(TrustingValidator.Statement, replaced.GetProperty("software_statement").GetString());
    }

    private static async Task<(string ClientId, string Rat, JsonElement Created)> RegisterAsync(
        HttpClient                  client,
        Dictionary<string, object?> body
    ) {
        var (status, payload) = await PostRawAsync(client, body);
        Assert.True(HttpStatusCode.Created == status, $"{(int)status}: {payload}");
        var root = JsonDocument.Parse(payload).RootElement.Clone();
        return (root.GetProperty("client_id").GetString()!,
            root.GetProperty("registration_access_token").GetString()!, root);
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostRawAsync(HttpClient client, Dictionary<string, object?> body) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/register") {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new("Bearer", "initial-token");
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<(HttpStatusCode Status, string Body, string? WwwAuthenticate)> ReplaceAsync(
        HttpClient                  client,
        string                      clientId,
        Dictionary<string, object?> body,
        string?                     rat
    ) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/connect/register/{clientId}") {
            Content = JsonContent.Create(body),
        };
        if (rat is not null) {
            request.Headers.Authorization = new("Bearer", rat);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(),
            response.Headers.WwwAuthenticate.ToString() is { Length: > 0 } challenge ? challenge : null);
    }

    private sealed class TrustingValidator : ISoftwareStatementValidator
    {
        public const string Statement = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJzb2Z0d2FyZSJ9.c2ln";

        public Task<SoftwareStatementValidationResult> ValidateAndExtractAsync(string softwareStatement, CancellationToken ct = default) {
            IDictionary<string, JsonElement>? claims = softwareStatement == Statement
                ? new Dictionary<string, JsonElement> {
                    ["client_name"] = JsonSerializer.Deserialize<JsonElement>("\"Trusted RP\""),
                    ["client_id"]   = JsonSerializer.Deserialize<JsonElement>("\"forged-id\""),
                }
                : null;
            return Task.FromResult(claims is null ? SoftwareStatementValidationResult.Unapproved : SoftwareStatementValidationResult.Approved(claims));
        }
    }
}
