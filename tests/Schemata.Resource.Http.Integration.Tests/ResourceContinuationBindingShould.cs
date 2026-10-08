using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

/// <summary>
///     Issue #194: a List continuation binds the issuing tenant, caller identity, and query
///     parameters. Replaying it under another principal, tenant, or parameter set fails with the
///     same invalid-token error as a malformed token, while a same-scope continuation paginates
///     without gaps or duplicates.
/// </summary>
[Trait("Category", "Integration")]
public class ResourceContinuationBindingShould : IClassFixture<WebAppFactory>
{
    private static readonly Guid TenantA = Guid.Parse("19419419-4194-4194-8194-194194194190");
    private static readonly Guid TenantB = Guid.Parse("19419419-4194-4194-8194-194194194191");

    private readonly WebAppFactory _factory;

    public ResourceContinuationBindingShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Get_ContinuationReplayedByAnotherPrincipal_ReturnsInvalidPageToken() {
        using var client = _factory.CreateClient();
        var prefix = $"BindingPrincipal{Guid.NewGuid():n}";
        await SeedLockedStudentsAsync(prefix, 2);

        var query = LockedQuery(prefix, 1);
        var first = await GetPageAsync(client, query, subject: "alice");
        var token = first.GetProperty("next_page_token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var replayed = await SendAsync(client, query + "&page_token=" + Uri.EscapeDataString(token!), subject: "bob");
        await AssertInvalidPageTokenAsync(replayed);
    }

    [Fact]
    public async Task Get_ContinuationReplayedByAnotherTenant_ReturnsInvalidPageToken() {
        using var client = _factory.CreateClient();
        var prefix = $"BindingTenant{Guid.NewGuid():n}";
        await SeedStudentsAsync(client, prefix, 2);

        var query = StudentsQuery(prefix, 1);
        var first = await GetPageAsync(client, query, tenant: TenantA);
        var token = first.GetProperty("next_page_token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var replayed = await SendAsync(client, query + "&page_token=" + Uri.EscapeDataString(token!), tenant: TenantB);
        await AssertInvalidPageTokenAsync(replayed);
    }

    [Fact]
    public async Task Get_ContinuationReplayedWithAlteredParameters_ReturnsInvalidPageToken() {
        using var client = _factory.CreateClient();
        var prefix = $"BindingFilter{Guid.NewGuid():n}";
        await SeedStudentsAsync(client, prefix, 2);

        var first = await GetPageAsync(client, StudentsQuery(prefix, 1));
        var token = first.GetProperty("next_page_token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var altered = StudentsQuery($"{prefix}-altered", 1) + "&page_token=" + Uri.EscapeDataString(token!);
        await AssertInvalidPageTokenAsync(await SendAsync(client, altered));
    }

    [Fact]
    public async Task Get_ContinuationWithinSameScope_PaginatesWithoutGapsOrDuplicates() {
        using var client = _factory.CreateClient();
        var prefix  = $"BindingScope{Guid.NewGuid():n}";
        var seeded  = await SeedLockedStudentsAsync(prefix, 3);
        var query   = LockedQuery(prefix, 1);
        var visited = new List<string>();
        string? token = null;
        do {
            var page = await GetPageAsync(client,
                token is null ? query : query + "&page_token=" + Uri.EscapeDataString(token),
                subject: "alice", tenant: TenantA);
            visited.AddRange(page.GetProperty("locked_students").EnumerateArray()
                                 .Select(row => row.GetProperty("name").GetString()!));
            token = page.TryGetProperty("next_page_token", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrEmpty(token));

        Assert.Equal(seeded.OrderBy(name => name, StringComparer.Ordinal), visited.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(visited.Count, visited.Distinct().Count());
    }

    private async Task<List<string>> SeedLockedStudentsAsync(string prefix, int count) {
        using var scope = _factory.Services.CreateScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>()
                                        .CreateDbContextAsync();
        var names = new List<string>();
        for (var i = 0; i < count; i++) {
            var name = $"lockedStudents/{prefix}-{i:d2}";
            db.LockedStudents.Add(new() { FullName = $"{prefix}-{i:d2}", Name = name, CanonicalName = name });
            names.Add(name);
        }

        await db.SaveChangesAsync();
        return names;
    }

    private static async Task SeedStudentsAsync(HttpClient client, string prefix, int count) {
        for (var i = 0; i < count; i++) {
            var created = await client.PostAsync("/v1/students",
                new StringContent(JsonSerializer.Serialize(new { full_name = $"{prefix}-{i:d2}" }),
                                  System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
    }

    private static string LockedQuery(string prefix, int pageSize) =>
        "/v1/lockedStudents?filter=" + Uri.EscapeDataString($"full_name:\"{prefix}\"")
      + $"&order_by=name%20asc&page_size={pageSize}";

    private static string StudentsQuery(string prefix, int pageSize) =>
        "/v1/students?filter=" + Uri.EscapeDataString($"full_name:\"{prefix}\"")
      + $"&order_by=name%20asc&page_size={pageSize}";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string query, string? subject = null,
        Guid? tenant = null) {
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        if (subject is not null) {
            request.Headers.Add("X-Test-Auth", "valid");
            request.Headers.Add("X-Test-Subject", subject);
        }

        if (tenant is not null) {
            request.Headers.Add("X-Test-Tenant", tenant.Value.ToString());
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string query, string? subject = null,
        Guid? tenant = null) {
        var response = await SendAsync(client, query, subject, tenant);
        Assert.True(HttpStatusCode.OK == response.StatusCode,
            $"status={response.StatusCode} body={await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertInvalidPageTokenAsync(HttpResponseMessage response) {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var violation = Assert.Single(body.GetProperty("error").GetProperty("details").EnumerateArray()
                                            .Where(d => d.TryGetProperty("field_violations", out _))
                                            .SelectMany(d => d.GetProperty("field_violations").EnumerateArray()));
        Assert.Equal("page_token", violation.GetProperty("field").GetString());
        Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, violation.GetProperty("reason").GetString());
    }
}
