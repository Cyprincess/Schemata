using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Identity.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantRefreshShould
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const string TenantA = "11111111-1111-1111-1111-111111111111";
    private const string TenantB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task Refresh_Only_Inside_The_Issuing_Tenant_Profile() {
        using var factory = new WebAppFactory().WithTenancy();
        var name = await CreateUserAsync(factory);
        using var issuer = Client(factory, TenantA);
        using var login = await issuer.PostAsJsonAsync("/Authenticate/Login", new LoginRequest {
            Username = name, Password = "Valid-Pass123!",
        }, Wire);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var issued = await TokensAsync(login);
        using var renewed = await issuer.PostAsJsonAsync("/Authenticate/Refresh", new RefreshRequest {
            RefreshToken = issued.refreshToken!,
        }, Wire);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await TokensAsync(renewed)).refreshToken));

        using var sibling = Client(factory, TenantB);
        using var rejected = await sibling.PostAsJsonAsync("/Authenticate/Refresh", new RefreshRequest {
            RefreshToken = issued.refreshToken!,
        }, Wire);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        using var global = Client(factory, null);
        using var rejectedGlobal = await global.PostAsJsonAsync("/Authenticate/Refresh", new RefreshRequest {
            RefreshToken = issued.refreshToken!,
        }, Wire);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedGlobal.StatusCode);
    }

    private static HttpClient Client(WebAppFactory factory, string? tenant) {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (tenant is not null) {
            client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
        }

        return client;
    }

    private static async Task<(string? accessToken, string? refreshToken)> TokensAsync(HttpResponseMessage response) {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.TryGetProperty("access_token", out var access) ? access.GetString() : null,
            json.RootElement.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null);
    }

    private static async Task<string> CreateUserAsync(WebAppFactory factory) {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var user = new SchemataUser { UserName = "tenant-refresh-" + Guid.NewGuid().ToString("N"), LockoutEnabled = true };
        Assert.True((await users.CreateAsync(user, "Valid-Pass123!")).Succeeded);
        return user.UserName;
    }
}
