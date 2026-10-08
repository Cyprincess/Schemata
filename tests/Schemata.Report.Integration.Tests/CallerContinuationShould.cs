using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Handlers;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Grpc;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class CallerContinuationShould
{
    private const string Snapshot = "reports/A/snapshots/daily";

    [Theory]
    [InlineData("bob")]
    [InlineData("anonymous")]
    [InlineData("anonymous-name")]
    [InlineData("other-scheme")]
    [InlineData("unidentified")]
    [InlineData("canonical-alice")]
    [InlineData("other-name-claim")]
    [InlineData("default-anonymous")]
    public async Task Rejects_Name_Only_Caller_Changes_Before_Snapshot_Store_Reads(string caller) {
        var keys = Directory.CreateTempSubdirectory("report-caller-");
        try {
            await using var fixture = Fixture(keys);
            await fixture.CreateAsync();
            using var scope = fixture.Services.CreateScope();
            var real = scope.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
            var store = new Mock<IReportSnapshotStore>(MockBehavior.Strict);
            store.Setup(value => value.GetAsync(Snapshot, It.IsAny<CancellationToken>()))
                .Returns((string name, CancellationToken ct) => real.GetAsync(name, ct));
            store.Setup(value => value.GetChunkAsync(Snapshot, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((string name, int index, CancellationToken ct) => real.GetChunkAsync(name, index, ct));
            var handler = new ReadSnapshotHandler<SchemataReportSnapshot>(store.Object,
                Options.Create(new SchemataReportOptions()), fixture.Services.GetRequiredService<IDataProtectionProvider>());
            var first = await handler.HandleAsync(Request(Principal("alice")));
            Assert.Equal(1, Value(Assert.Single(first.Rows)));
            Assert.NotNull(first.NextPageToken);
            store.Invocations.Clear();
            var error = await Assert.ThrowsAsync<InvalidArgumentException>(() => handler.HandleAsync(Request(Principal(caller), first.NextPageToken)));
            Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
            store.VerifyNoOtherCalls();
        } finally { keys.Delete(true); }
    }

    [Fact]
    public async Task Shares_Name_Only_Caller_Binding_Across_HTTP_Grpc_And_Local_Reads() {
        var keys = Directory.CreateTempSubdirectory("report-caller-");
        try {
            await using var fixture = Fixture(keys);
            await fixture.CreateAsync();
            using var client = fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Caller", "alice");
            using var first = await client.GetAsync("/v1/" + Snapshot + ":read?page_size=1");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var page = await first.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, Assert.Single(page.GetProperty("rows").EnumerateArray()).GetProperty("value").GetInt32());
            var token = page.GetProperty("next_page_token").GetString()!;
            var second = await ReadGrpc(fixture, token, "alice-shadow");
            Assert.Equal(2L, Assert.Single(second.Rows).Fields["value"].IntValue);
            using var scope = fixture.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ReadSnapshotHandler<SchemataReportSnapshot>>();
            var third = await handler.HandleAsync(Request(Principal("alice"), second.NextPageToken));
            Assert.Equal(3, Value(Assert.Single(third.Rows)));
            Assert.Null(third.NextPageToken);
        } finally { keys.Delete(true); }
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("anonymous")]
    public async Task Rejects_HTTP_And_Grpc_Continuation_Resumption_By_Other_Callers(string caller) {
        var keys = Directory.CreateTempSubdirectory("report-caller-");
        try {
            await using var fixture = Fixture(keys);
            await fixture.CreateAsync();
            using var client = fixture.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Caller", "alice");
            using var first = await client.GetAsync("/v1/" + Snapshot + ":read?page_size=1");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var page = await first.Content.ReadFromJsonAsync<JsonElement>();
            var token = page.GetProperty("next_page_token").GetString()!;
            client.DefaultRequestHeaders.Remove("X-Test-Caller");
            client.DefaultRequestHeaders.Add("X-Test-Caller", caller);
            using var rejected = await client.GetAsync("/v1/" + Snapshot + ":read?page_size=1&page_token=" + Uri.EscapeDataString(token));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("INVALID_ARGUMENT", body.GetProperty("error").GetProperty("status").GetString());
            var error = await Assert.ThrowsAsync<RpcException>(() => ReadGrpc(fixture, token, caller));
            Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        } finally { keys.Delete(true); }
    }

    [Theory]
    [InlineData("canonical-id-to-sub")]
    [InlineData("name-with-blank-id")]
    [InlineData("anonymous-named")]
    public async Task Preserves_Canonical_Subject_Precedence_And_Anonymous_Equivalence(string identity) {
        var keys = Directory.CreateTempSubdirectory("report-caller-");
        try {
            await using var fixture = Fixture(keys);
            await fixture.CreateAsync();
            using var scope = fixture.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ReadSnapshotHandler<SchemataReportSnapshot>>();
            var firstCaller = identity switch {
                "canonical-id-to-sub" => new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "Alice"), new Claim("sub", "Bob"), new Claim(ClaimTypes.Name, "display"),
                ], "test")),
                "name-with-blank-id" => new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, " "), new Claim("sub", ""), new Claim(ClaimTypes.Name, "Alice"),
                ], "test")),
                _ => Principal("anonymous"),
            };
            var nextCaller = identity == "canonical-id-to-sub"
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "Alice")], "test"))
                : Principal(identity == "anonymous-named" ? "anonymous-name" : "alice");
            var first = await handler.HandleAsync(Request(firstCaller));
            var second = await handler.HandleAsync(Request(nextCaller, first.NextPageToken));
            Assert.Equal(1, Value(Assert.Single(first.Rows)));
            Assert.Equal(2, Value(Assert.Single(second.Rows)));
        } finally { keys.Delete(true); }
    }

    [Fact]
    public async Task Allows_Unidentified_Authenticated_Terminal_Read_But_Rejects_Continuation_Creation() {
        var keys = Directory.CreateTempSubdirectory("report-caller-");
        try {
            await using var fixture = Fixture(keys);
            await fixture.CreateAsync();
            using var scope = fixture.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ReadSnapshotHandler<SchemataReportSnapshot>>();
            var terminal = await handler.HandleAsync(new() { CanonicalName = Snapshot, PageSize = 10, Principal = Principal("unidentified") });
            Assert.Equal(new[] { 1, 2, 3 }, terminal.Rows.Select(Value));
            Assert.Null(terminal.NextPageToken);
            var error = await Assert.ThrowsAsync<InvalidArgumentException>(() => handler.HandleAsync(Request(Principal("unidentified"))));
            Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);
        } finally { keys.Delete(true); }
    }

    private static SnapshotRelationFixture Fixture(DirectoryInfo keys) => new(services => {
        services.AddDataProtection().PersistKeysToFileSystem(keys).SetApplicationName("ReportCallerContinuations");
        services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new CallerFilter()));
    });

    private static ReadSnapshotRequest Request(ClaimsPrincipal principal, string? token = null) => new() {
        CanonicalName = Snapshot, PageSize = 1, PageToken = token, Principal = principal,
    };

    private static ClaimsPrincipal Principal(string caller) {
        if (caller == "canonical-alice") return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "Alice")], "test"));
        if (caller == "other-name-claim") return new(new ClaimsIdentity([new Claim("custom-name", "Alice")], "test", "custom-name", ClaimTypes.Role));
        var identity = new ClaimsIdentity(caller is "unidentified" or "anonymous" ? [] : [new Claim(ClaimTypes.Name, caller == "bob" ? "Bob" : "Alice")],
            caller is "anonymous" or "anonymous-name" or "default-anonymous" ? null : caller == "other-scheme" ? "other" : "test");
        var principal = new ClaimsPrincipal(identity);
        if (caller is "alice-shadow" or "default-anonymous") {
            principal.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "Bob")], caller == "default-anonymous" ? "test" : null));
        }
        return principal;
    }

    private static int Value(System.Collections.Generic.IReadOnlyDictionary<string, object?> row) => ((JsonElement)row["value"]!).GetInt32();

    private static async Task<ReadSnapshotGrpcResponse> ReadGrpc(SnapshotRelationFixture fixture, string token, string caller) {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ReadSnapshotRequest));
        model.Add(typeof(ReadSnapshotGrpcResponse), true);
        var descriptor = ResourceNameDescriptor.ForType<SchemataReportSnapshot>();
        var method = new Method<ReadSnapshotRequest, ReadSnapshotGrpcResponse>(MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(SchemataReportSnapshot)),
            GrpcResourceNaming.CustomMethodName(descriptor, "read"),
            GrpcMarshallers.Create<ReadSnapshotRequest>(model), GrpcMarshallers.Create<ReadSnapshotGrpcResponse>(model));
        using var channel = fixture.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null,
            new(new Metadata { { "x-test-caller", caller } }), new() { CanonicalName = Snapshot, PageSize = 1, PageToken = token });
        return await call.ResponseAsync;
    }

    private sealed class CallerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
            app.Use(async (context, continuation) => {
                context.User = Principal(context.Request.Headers["X-Test-Caller"].ToString());
                await continuation();
            });
            next(app);
        };
    }
}
