using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Control;
using Xunit;

namespace Schemata.Quota.Http.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class QuotaControlShould
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Testing").UseContentRoot(AppContext.BaseDirectory);
    }

    [Fact]
    public async Task Authentication_And_Operation_Policies_Gate_Control_Calls() {
        using var factory = new Factory();
        using var anonymous = factory.CreateClient();
        using var challenge = await anonymous.PostAsync("/v1/quota:reserve", ReservePayload("anonymous"));
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);

        using var deniedClient = Client(factory, "alice");
        using var denied = await deniedClient.PostAsync("/v1/quota:reserve", ReservePayload("denied"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var reserveOnly = Client(factory, "alice", QuotaPolicies.Reserve);
        using var reserved = await reserveOnly.PostAsync("/v1/quota:reserve", ReservePayload("authorized"));
        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        var uid = await ReservationUidAsync(reserved);
        using var commitDenied = await reserveOnly.PostAsync("/v1/quota:commit", SettlePayload(uid));
        using var releaseDenied = await reserveOnly.PostAsync("/v1/quota:release", SettlePayload(uid));
        using var ledgerDenied = await reserveOnly.GetAsync("/v1/quota/ledger");
        Assert.Equal(HttpStatusCode.Forbidden, commitDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, releaseDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ledgerDenied.StatusCode);
    }

    [Fact]
    public async Task Forbidden_Scope_And_Tenant_Values_Are_Rejected_Before_Dispatch() {
        using var factory = new Factory();
        using var client = Client(factory, "alice", AllPermissions());
        using var reserveScope = await client.PostAsync("/v1/quota:reserve", ReservePayload("scope", scope: "other"));
        using var reserveTenant = await client.PostAsync("/v1/quota:reserve", ReservePayload("tenant", tenant: "other"));
        using var commitScope = await client.PostAsync("/v1/quota:commit", SettlePayload(Guid.NewGuid(), scope: "other"));
        using var releaseTenant = await client.PostAsync("/v1/quota:release", SettlePayload(Guid.NewGuid(), tenant: "other"));
        using var ledgerScope = await client.GetAsync("/v1/quota/ledger?scope=other");
        using var ledgerTenant = await client.GetAsync("/v1/quota/ledger?tenant=other");

        Assert.Equal(HttpStatusCode.BadRequest, reserveScope.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reserveTenant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, commitScope.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, releaseTenant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ledgerScope.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ledgerTenant.StatusCode);
        Assert.Equal(0, await CountReservationsAsync(factory));
    }

    [Fact]
    public async Task Reserve_Settle_And_Ledger_Expose_The_Same_Accounting_State() {
        using var factory = new Factory();
        using var client = Client(factory, "alice", AllPermissions());
        using var reserved = await client.PostAsync("/v1/quota:reserve", ReservePayload("commit-me", amount: 2));
        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        using var reservation = await JsonAsync(reserved);
        var uid = reservation.RootElement.GetProperty("reservation_uid").GetGuid();
        Assert.Equal("pending", reservation.RootElement.GetProperty("state").GetString());
        var account = Assert.Single(reservation.RootElement.GetProperty("accounts").EnumerateArray());
        Assert.Equal(2, Int64(account.GetProperty("reserved")));
        Assert.Equal(8, Int64(account.GetProperty("available")));

        using var committed = await client.PostAsync("/v1/quota:commit", SettlePayload(uid));
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        using var settled = await JsonAsync(committed);
        Assert.Equal("committed", settled.RootElement.GetProperty("state").GetString());
        Assert.Equal(2, Int64(Assert.Single(settled.RootElement.GetProperty("accounts").EnumerateArray()).GetProperty("committed")));

        using var second = await client.PostAsync("/v1/quota:reserve", ReservePayload("release-me"));
        var secondUid = await ReservationUidAsync(second);
        using var released = await client.PostAsync("/v1/quota:release", SettlePayload(secondUid));
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        using var ledgerResponse = await client.GetAsync("/v1/quota/ledger?metric=calls&page_size=200");
        Assert.Equal(HttpStatusCode.OK, ledgerResponse.StatusCode);
        using var ledger = await JsonAsync(ledgerResponse);
        var entries = ledger.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "commit" && Int64(entry.GetProperty("amount")) == 2);
        Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "release" && entry.GetProperty("reservation_uid").GetGuid() == secondUid);

        using var bounded = await client.GetAsync("/v1/quota/ledger?page_size=201");
        Assert.Equal(HttpStatusCode.BadRequest, bounded.StatusCode);
    }

    [Fact]
    public async Task Delegation_And_Cross_Subject_Settlement_Are_Denied() {
        using var factory = new Factory();
        using var alice = Client(factory, "alice", AllPermissions());
        using var reserved = await alice.PostAsync("/v1/quota:reserve", ReservePayload("private"));
        var uid = await ReservationUidAsync(reserved);
        using var delegated = await alice.PostAsync("/v1/quota:reserve", ReservePayload("delegated", requestedSubject: "bob"));
        Assert.Equal(HttpStatusCode.Forbidden, delegated.StatusCode);

        using var bob = Client(factory, "bob", AllPermissions());
        using var stolen = await bob.PostAsync("/v1/quota:commit", SettlePayload(uid));
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        using var delegatedLedger = await bob.GetAsync("/v1/quota/ledger?requested_subject=alice");
        Assert.Equal(HttpStatusCode.Forbidden, delegatedLedger.StatusCode);
        using var ledger = await bob.GetAsync("/v1/quota/ledger");
        Assert.Equal(HttpStatusCode.OK, ledger.StatusCode);
        using var body = await JsonAsync(ledger);
        Assert.Empty(body.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal(1, (await AccountsAsync(factory)).Single().Reserved);
    }

    [Fact]
    public async Task Capacity_Errors_Preserve_Quota_And_Retry_Details() {
        using var factory = new Factory();
        using var client = Client(factory, "alice", AllPermissions());
        using var first = await client.PostAsync("/v1/quota:reserve", ReservePayload("full", limit: 1));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var rejected = await client.PostAsync("/v1/quota:reserve", ReservePayload("overflow", limit: 1));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Headers.RetryAfter?.ToString()));
        using var body = await JsonAsync(rejected);
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("RESOURCE_EXHAUSTED", error.GetProperty("status").GetString());
        var details = error.GetProperty("details").EnumerateArray().ToArray();
        var quota = Assert.Single(details, detail => detail.TryGetProperty("violations", out _));
        Assert.Equal("calls", Assert.Single(quota.GetProperty("violations").EnumerateArray()).GetProperty("quota_metric").GetString());
        Assert.Contains(details, detail => detail.TryGetProperty("retry_delay", out _));
        Assert.Equal(1, (await AccountsAsync(factory)).Single().Reserved);
    }

    [Fact]
    public async Task Oversized_Token_Charge_Returns_Capacity_Error_With_Bounded_Retry() {
        using var factory = new Factory();
        using var client = Client(factory, "alice", AllPermissions());
        using var response = await client.PostAsync("/v1/quota:reserve", Json(new {
            operation_id = "huge", charges = new[] { new {
                policy = new { name = "tokens", version = 1, algorithm = QuotaConstants.Algorithms.TokenBucket,
                    limit = 1, period = "365.00:00:00", burst = 1 }, metric = "tokens", amount = 1_000_000_000L,
            } },
        }));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var body = await JsonAsync(response);
        Assert.Equal("RESOURCE_EXHAUSTED", body.RootElement.GetProperty("error").GetProperty("status").GetString());
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(TimeSpan.FromSeconds(int.MaxValue), response.Headers.RetryAfter.Delta);
        Assert.Equal(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(response.Headers.GetValues("Retry-After")));
        var details = body.RootElement.GetProperty("error").GetProperty("details").EnumerateArray();
        Assert.Single(details, detail => detail.TryGetProperty("retry_delay", out _));
        var retry = details.Single(detail => detail.TryGetProperty("retry_delay", out _));
        Assert.Equal(TimeSpan.MaxValue, TimeSpan.Parse(retry.GetProperty("retry_delay").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Empty(await AccountsAsync(factory));
    }

    [Fact]
    public async Task Missing_Scope_Resolver_Fails_Closed() {
        using var factory = new Factory().WithWebHostBuilder(builder => builder.UseEnvironment("MissingScope"));
        using var client = Client(factory, "alice", AllPermissions());
        using var response = await client.PostAsync("/v1/quota:reserve", ReservePayload("unresolved"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        using var body = await JsonAsync(response);
        Assert.Equal("FAILED_PRECONDITION", body.RootElement.GetProperty("error").GetProperty("status").GetString());
        Assert.Equal(0, await CountReservationsAsync(factory));
    }

    [Fact]
    public async Task Remote_Accounting_Commits_Independently_Of_A_Caller_Transaction() {
        using var factory = new Factory();
        using var client = Client(factory, "alice", AllPermissions());
        using (var response = await client.PostAsync("/v1/quota:reserve", ReservePayload("remote"))) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, await CountReservationsAsync(factory));
        using var caller = factory.Services.CreateScope();
        var transaction = caller.ServiceProvider.GetRequiredService<IQuotaStore>().Begin(IsolationLevel.Unspecified);
        await using (transaction) {
            await transaction.RollbackAsync();
        }

        Assert.Equal(1, await CountReservationsAsync(factory));
        Assert.Equal(1, (await AccountsAsync(factory)).Single().Reserved);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string subject, params string[] permissions) {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Subject", subject);
        if (permissions.Length > 0) client.DefaultRequestHeaders.Add("X-Permissions", string.Join(",", permissions));
        return client;
    }

    private static string[] AllPermissions() => [QuotaPolicies.Reserve, QuotaPolicies.Commit, QuotaPolicies.Release, QuotaPolicies.Ledger];

    private static StringContent ReservePayload(
        string operationId,
        long amount = 1,
        long limit = 10,
        string? requestedSubject = null,
        string? scope = null,
        string? tenant = null
    ) {
        var payload = new Dictionary<string, object?> {
            ["operation_id"] = operationId,
            ["requested_subject"] = requestedSubject,
            ["charges"] = new[] {
                new {
                    policy = new {
                        name = "requests", version = 1, algorithm = QuotaConstants.Algorithms.FixedWindow,
                        limit, period = "00:01:00", burst = limit,
                    },
                    metric = "calls", amount,
                },
            },
            ["expires_at"] = null,
        };
        if (scope is not null) payload["scope"] = scope;
        if (tenant is not null) payload["tenant"] = tenant;
        return Json(payload);
    }

    private static StringContent SettlePayload(Guid uid, string? scope = null, string? tenant = null) {
        var payload = new Dictionary<string, object?> { ["reservation_uid"] = uid };
        if (scope is not null) payload["scope"] = scope;
        if (tenant is not null) payload["tenant"] = tenant;
        return Json(payload);
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<Guid> ReservationUidAsync(HttpResponseMessage response) {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await JsonAsync(response);
        return body.RootElement.GetProperty("reservation_uid").GetGuid();
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static long Int64(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? long.Parse(value.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture)
        : value.GetInt64();

    private static async Task<int> CountReservationsAsync(WebApplicationFactory<Program> factory) {
        using var scope = factory.Services.CreateScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<QuotaTransportDbContext>>();
        await using var database = await contexts.CreateDbContextAsync();
        return await database.Reservations.CountAsync();
    }

    private static async Task<List<Schemata.Quota.Skeleton.Entities.SchemataQuotaAccount>> AccountsAsync(
        WebApplicationFactory<Program> factory
    ) {
        using var scope = factory.Services.CreateScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<QuotaTransportDbContext>>();
        await using var database = await contexts.CreateDbContextAsync();
        return await database.Accounts.ToListAsync();
    }
}
