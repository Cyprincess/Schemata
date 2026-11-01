using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     Authorization-scheme handling on the dynamic registration endpoints, per RFC 9110 §11.1
///     (case-insensitive auth-scheme token) over RFC 6750 §2.1 (case-sensitive b64token
///     credential): "bearer" carries the same privileges as "Bearer", while a malformed or
///     duplicated Authorization field is rejected with 401 invalid_token instead of being
///     downgraded to anonymous (RFC 9110 §5.2, §11.4; RFC 6750 §3.1).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class RegisterManagementAuthShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task Accept_A_Lowercase_Scheme_On_Register_And_On_A_Management_Read() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        using var register = new HttpRequestMessage(HttpMethod.Post, "/connect/register") {
            Content = JsonContent.Create(new Dictionary<string, object?> {
                ["redirect_uris"] = new[] { "https://rp.example/cb" },
            }),
        };
        register.Headers.TryAddWithoutValidation("Authorization", "bearer initial-token");
        using var created = await client.SendAsync(register);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var payload = await created.Content.ReadFromJsonAsync<JsonElement>();
        var clientId = payload.GetProperty("client_id").GetString();
        var rat      = payload.GetProperty("registration_access_token").GetString();
        Assert.NotNull(clientId);
        Assert.NotNull(rat);

        using var read = new HttpRequestMessage(HttpMethod.Get, $"/connect/register/{clientId}");
        read.Headers.TryAddWithoutValidation("Authorization", $"bearer {rat}");
        using var readResponse = await client.SendAsync(read);

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var current = await readResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(clientId, current.GetProperty("client_id").GetString());
    }

    [Fact]
    public async Task Reject_A_Bearer_Field_Without_A_Credential_As_Invalid_Token() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        using var read = new HttpRequestMessage(HttpMethod.Get, "/connect/register/no-such-client");
        read.Headers.TryAddWithoutValidation("Authorization", "Bearer");
        using var response = await client.SendAsync(read);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.StartsWith("Bearer", challenge, StringComparison.Ordinal);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_token", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Reject_Duplicated_Authorization_Field_Lines_As_Invalid_Token() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        using var read = new HttpRequestMessage(HttpMethod.Get, "/connect/register/no-such-client");
        read.Headers.TryAddWithoutValidation("Authorization", "Bearer one");
        read.Headers.TryAddWithoutValidation("Authorization", "Bearer two");
        using var response = await client.SendAsync(read);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.StartsWith("Bearer", challenge, StringComparison.Ordinal);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_token", error.GetProperty("error").GetString());
    }
}
