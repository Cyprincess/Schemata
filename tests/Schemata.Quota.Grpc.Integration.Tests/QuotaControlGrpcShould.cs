using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Rpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Control;
using Xunit;

namespace Schemata.Quota.Grpc.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class QuotaControlGrpcShould
{
    private sealed class Factory(string environment = "Testing") : WebApplicationFactory<Program>
    {
        public List<GrpcChannel> Channels { get; } = [];
        public List<HttpClient> Clients { get; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(environment).UseContentRoot(AppContext.BaseDirectory);

        protected override void Dispose(bool disposing) {
            if (disposing) {
                foreach (var channel in Channels) channel.Dispose();
                foreach (var client in Clients) client.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task Authentication_And_Operation_Policies_Gate_Grpc_Calls() {
        using var factory = new Factory();
        var anonymous = Grpc(factory, subject: null);
        var challenge = await Assert.ThrowsAsync<RpcException>(() => anonymous.ReserveAsync(Command("anonymous")).AsTask());
        Assert.Equal(StatusCode.Unauthenticated, challenge.StatusCode);

        var deniedClient = Grpc(factory, "alice");
        var denied = await Assert.ThrowsAsync<RpcException>(() => deniedClient.ReserveAsync(Command("denied")).AsTask());
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);

        var reserveOnly = Grpc(factory, "alice", QuotaPolicies.Reserve);
        var reserved = await reserveOnly.ReserveAsync(Command("authorized"));
        var commitDenied = await Assert.ThrowsAsync<RpcException>(() => reserveOnly.CommitAsync(Settle(reserved.ReservationUid)).AsTask());
        var releaseDenied = await Assert.ThrowsAsync<RpcException>(() => reserveOnly.ReleaseAsync(Settle(reserved.ReservationUid)).AsTask());
        var ledgerDenied = await Assert.ThrowsAsync<RpcException>(() => reserveOnly.ListLedgerAsync(new()).AsTask());
        Assert.Equal(StatusCode.PermissionDenied, commitDenied.StatusCode);
        Assert.Equal(StatusCode.PermissionDenied, releaseDenied.StatusCode);
        Assert.Equal(StatusCode.PermissionDenied, ledgerDenied.StatusCode);
    }

    [Fact]
    public async Task Forbidden_Scope_And_Tenant_Values_Are_Rejected_Before_Dispatch() {
        using var factory = new Factory();
        var client = Grpc(factory, "alice", AllPermissions());
        var reserveScope = await Assert.ThrowsAsync<RpcException>(() => client.ReserveAsync(Command("scope", scope: "other")).AsTask());
        var reserveTenant = await Assert.ThrowsAsync<RpcException>(() => client.ReserveAsync(Command("tenant", tenant: "other")).AsTask());
        var commitScope = await Assert.ThrowsAsync<RpcException>(() => client.CommitAsync(Settle(Guid.NewGuid().ToString(), scope: "other")).AsTask());
        var releaseTenant = await Assert.ThrowsAsync<RpcException>(() => client.ReleaseAsync(Settle(Guid.NewGuid().ToString(), tenant: "other")).AsTask());
        var ledgerScope = await Assert.ThrowsAsync<RpcException>(() => client.ListLedgerAsync(new() { Scope = "other" }).AsTask());
        var ledgerTenant = await Assert.ThrowsAsync<RpcException>(() => client.ListLedgerAsync(new() { Tenant = "other" }).AsTask());

        Assert.Equal(StatusCode.InvalidArgument, reserveScope.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, reserveTenant.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, commitScope.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, releaseTenant.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, ledgerScope.StatusCode);
        Assert.Equal(StatusCode.InvalidArgument, ledgerTenant.StatusCode);
        Assert.Equal(0, await CountReservationsAsync(factory));
    }

    [Fact]
    public async Task Mixed_Transport_Retry_Reuses_State_And_Rejects_Changed_Charges() {
        using var factory = new Factory();
        using var http = Http(factory, "alice", AllPermissions());
        var grpc = Grpc(factory, http);
        using var firstResponse = await http.PostAsync("/v1/quota:reserve", HttpReservePayload("shared", amount: 2));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var firstBody = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var firstUid = firstBody.RootElement.GetProperty("reservation_uid").GetGuid();

        var second = await grpc.ReserveAsync(Command("shared", amount: 2));
        Assert.Equal(firstUid.ToString(), second.ReservationUid);
        Assert.Equal(1, await CountReservationItemsAsync(factory, firstUid));
        Assert.Equal(1, await CountReservationsAsync(factory));
        Assert.Equal(2, (await AccountsAsync(factory)).Single().Reserved);

        var forbidden = await Assert.ThrowsAsync<RpcException>(() => grpc.ReserveAsync(Command("shared", amount: 2, scope: "other")).AsTask());
        Assert.Equal(StatusCode.InvalidArgument, forbidden.StatusCode);
        using var httpChanged = await http.PostAsync("/v1/quota:reserve", HttpReservePayload("shared", amount: 3));
        Assert.Equal(HttpStatusCode.Conflict, httpChanged.StatusCode);
        var grpcChanged = await Assert.ThrowsAsync<RpcException>(() => grpc.ReserveAsync(Command("shared", amount: 3)).AsTask());
        Assert.Equal(StatusCode.AlreadyExists, grpcChanged.StatusCode);
        Assert.Equal(1, await CountReservationsAsync(factory));
        Assert.Equal(2, (await AccountsAsync(factory)).Single().Reserved);

        var committed = await grpc.CommitAsync(Settle(second.ReservationUid));
        Assert.Equal(QuotaReservationState.Committed, committed.State);
        var releaseSeed = await grpc.ReserveAsync(Command("release-parity"));
        var released = await grpc.ReleaseAsync(Settle(releaseSeed.ReservationUid));
        Assert.Equal(QuotaReservationState.Released, released.State);
        var grpcLedger = await grpc.ListLedgerAsync(new() { Metric = "calls", PageSize = 200 });
        Assert.Equal(2, grpcLedger.Entries.Count);
        Assert.Contains(grpcLedger.Entries, entry => entry.Kind == QuotaSettlementKind.Commit && entry.Amount == 2);
        Assert.Contains(grpcLedger.Entries, entry => entry.Kind == QuotaSettlementKind.Release && entry.ReservationUid == releaseSeed.ReservationUid);

        using var httpLedger = await http.GetAsync("/v1/quota/ledger?metric=calls&page_size=200");
        Assert.Equal(HttpStatusCode.OK, httpLedger.StatusCode);
        using var ledgerBody = JsonDocument.Parse(await httpLedger.Content.ReadAsStringAsync());
        Assert.Equal(2, ledgerBody.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task Oversized_Token_Charge_Remains_ResourceExhausted() {
        using var factory = new Factory();
        var grpc = Grpc(factory, "alice", AllPermissions());
        var request = Command("oversized", amount: 1_000_000_000, limit: 1);
        request.Charges[0].Policy.Algorithm = QuotaConstants.Algorithms.TokenBucket;
        request.Charges[0].Policy.Period = TimeSpan.FromDays(365);

        var error = await Assert.ThrowsAsync<RpcException>(() => grpc.ReserveAsync(request).AsTask());

        Assert.Equal(StatusCode.ResourceExhausted, error.StatusCode);
        Assert.Empty(await AccountsAsync(factory));
    }

    [Fact]
    public async Task Delegation_And_Cross_Subject_Settlement_Are_Denied() {
        using var factory = new Factory();
        var alice = Grpc(factory, "alice", AllPermissions());
        var reserved = await alice.ReserveAsync(Command("private"));
        var delegated = await Assert.ThrowsAsync<RpcException>(() => alice.ReserveAsync(Command("delegated", requestedSubject: "bob")).AsTask());
        Assert.Equal(StatusCode.PermissionDenied, delegated.StatusCode);

        var bob = Grpc(factory, "bob", AllPermissions());
        var stolen = await Assert.ThrowsAsync<RpcException>(() => bob.CommitAsync(Settle(reserved.ReservationUid)).AsTask());
        Assert.Equal(StatusCode.NotFound, stolen.StatusCode);
        var ledger = await bob.ListLedgerAsync(new());
        Assert.Empty(ledger.Entries);
        Assert.Equal(1, (await AccountsAsync(factory)).Single().Reserved);
    }

    [Fact]
    public async Task Capacity_Errors_Preserve_Quota_And_Retry_Details() {
        using var factory = new Factory();
        var client = Grpc(factory, "alice", AllPermissions());
        _ = await client.ReserveAsync(Command("full", limit: 1));
        var error = await Assert.ThrowsAsync<RpcException>(() => client.ReserveAsync(Command("overflow", limit: 1)).AsTask());

        Assert.Equal(StatusCode.ResourceExhausted, error.StatusCode);
        var status = Google.Rpc.Status.Parser.ParseFrom(error.Trailers.GetValueBytes("grpc-status-details-bin"));
        var quota = status.Details.Single(detail => detail.Is(QuotaFailure.Descriptor)).Unpack<QuotaFailure>();
        Assert.Equal("calls", Assert.Single(quota.Violations).QuotaMetric);
        var retry = status.Details.Single(detail => detail.Is(RetryInfo.Descriptor)).Unpack<RetryInfo>();
        Assert.NotNull(retry.RetryDelay);
        Assert.Equal(1, (await AccountsAsync(factory)).Single().Reserved);
    }

    [Fact]
    public async Task Missing_Scope_Resolver_Fails_Closed() {
        using var factory = new Factory("MissingScope");
        var client = Grpc(factory, "alice", AllPermissions());
        var error = await Assert.ThrowsAsync<RpcException>(() => client.ReserveAsync(Command("unresolved")).AsTask());

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        Assert.Equal(0, await CountReservationsAsync(factory));
    }

    private static HttpClient Http(WebApplicationFactory<Program> factory, string? subject, params string[] permissions) {
        var client = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        if (factory is Factory owned) owned.Clients.Add(client);
        if (subject is not null) client.DefaultRequestHeaders.Add("X-Subject", subject);
        if (permissions.Length > 0) client.DefaultRequestHeaders.Add("X-Permissions", string.Join(",", permissions));
        return client;
    }

    private static IQuotaGrpcService Grpc(WebApplicationFactory<Program> factory, string? subject, params string[] permissions) =>
        Grpc(factory, Http(factory, subject, permissions));

    private static IQuotaGrpcService Grpc(WebApplicationFactory<Program> factory, HttpClient http) {
        var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        if (factory is Factory owned) owned.Channels.Add(channel);
        var model = factory.Services.GetRequiredService<QuotaGrpcModel>().Model;
        return channel.CreateGrpcService<IQuotaGrpcService>(ClientFactory.Create(BinderConfiguration.Create([
            ProtoBufMarshallerFactory.Create(model)])));
    }

    private static string[] AllPermissions() => [QuotaPolicies.Reserve, QuotaPolicies.Commit, QuotaPolicies.Release, QuotaPolicies.Ledger];

    private static ReserveQuotaCommand Command(
        string operationId,
        long amount = 1,
        long limit = 10,
        string? requestedSubject = null,
        string? scope = null,
        string? tenant = null
    ) => new() {
        OperationId = operationId,
        RequestedSubject = requestedSubject,
        Charges = {
            new() {
                Policy = new() {
                    Name = "requests", Version = 1, Algorithm = QuotaConstants.Algorithms.FixedWindow,
                    Limit = limit, Period = TimeSpan.FromMinutes(1), Burst = limit,
                },
                Metric = "calls", Amount = amount,
            },
        },
        Scope = scope,
        Tenant = tenant,
    };

    private static SettleQuotaCommand Settle(string uid, string? scope = null, string? tenant = null) => new() {
        ReservationUid = uid, Scope = scope, Tenant = tenant,
    };

    private static StringContent HttpReservePayload(string operationId, long amount) {
        var payload = new Dictionary<string, object?> {
            ["operation_id"] = operationId,
            ["charges"] = new[] {
                new {
                    policy = new {
                        name = "requests", version = 1, algorithm = QuotaConstants.Algorithms.FixedWindow,
                        limit = 10, period = "00:01:00", burst = 10,
                    },
                    metric = "calls", amount,
                },
            },
        };
        return new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    private static async Task<int> CountReservationsAsync(WebApplicationFactory<Program> factory) {
        using var scope = factory.Services.CreateScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<QuotaTransportDbContext>>();
        await using var database = await contexts.CreateDbContextAsync();
        return await database.Reservations.CountAsync();
    }

    private static async Task<int> CountReservationItemsAsync(WebApplicationFactory<Program> factory, Guid reservationUid) {
        using var scope = factory.Services.CreateScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<QuotaTransportDbContext>>();
        await using var database = await contexts.CreateDbContextAsync();
        return await database.ReservationItems.CountAsync(item => item.ReservationUid == reservationUid);
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
