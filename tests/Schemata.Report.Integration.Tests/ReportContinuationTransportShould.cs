using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Tenancy;
using Schemata.Common;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Grpc;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton.Entities;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class ReportContinuationTransportShould
{
    [Fact]
    public async Task Page_Real_SQLite_Rows_Over_HTTP_And_Reject_Cross_Target_Over_Grpc() {
        await using var fixture = new SnapshotRelationFixture();
        await fixture.CreateAsync();
        using var client = fixture.CreateClient();
        var values = new List<int>();
        string? token = null;
        string? firstToken = null;
        do {
            var suffix = token is null ? "" : "&page_token=" + Uri.EscapeDataString(token);
            using var response = await client.GetAsync("/v1/reports/A/snapshots/daily:read?page_size=1" + suffix);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<JsonElement>();
            values.AddRange(page.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("value").GetInt32()));
            token = page.TryGetProperty("next_page_token", out var next) ? next.GetString() : null;
            firstToken ??= token;
        } while (token is not null);
        Assert.Equal(new[] { 1, 2, 3 }, values);
        Assert.NotNull(firstToken);
        var grpcValues = new List<long?>();
        token = null;
        do {
            var page = await ReadGrpc(fixture, new() { CanonicalName = "reports/A/snapshots/daily", PageSize = 1, PageToken = token });
            grpcValues.AddRange(page.Rows.Select(row => row.Fields["value"].IntValue));
            token = page.NextPageToken;
        } while (token is not null);
        Assert.Equal(new long?[] { 1, 2, 3 }, grpcValues);
        using var wrongTarget = await client.GetAsync("/v1/reports/B/snapshots/daily:read?page_size=1&page_token=" + Uri.EscapeDataString(firstToken));
        Assert.Equal(HttpStatusCode.BadRequest, wrongTarget.StatusCode);
        var body = await wrongTarget.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_ARGUMENT", body.GetProperty("error").GetProperty("status").GetString());
        var error = await Assert.ThrowsAsync<RpcException>(() => ReadGrpc(fixture, new() {
            CanonicalName = "reports/B/snapshots/daily", PageSize = 1, PageToken = firstToken,
        }));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("tenant")]
    public async Task Reject_Changed_Server_Caller_Context_Over_HTTP_And_Grpc(string changed) {
        await using var fixture = new SnapshotRelationFixture(services =>
            services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new CallerFilter())));
        await fixture.CreateAsync();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", "alice");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "11111111-1111-1111-1111-111111111111");
        using var first = await client.GetAsync("/v1/reports/A/snapshots/daily:read?page_size=1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = await first.Content.ReadFromJsonAsync<JsonElement>();
        var token = page.GetProperty("next_page_token").GetString()!;
        var subject = changed == "subject" ? "bob" : "alice";
        var tenant = changed == "tenant" ? "22222222-2222-2222-2222-222222222222" : "11111111-1111-1111-1111-111111111111";
        client.DefaultRequestHeaders.Remove("X-Test-Subject");
        client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        client.DefaultRequestHeaders.Remove("X-Test-Tenant");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
        using var rejected = await client.GetAsync("/v1/reports/A/snapshots/daily:read?page_size=1&page_token=" + Uri.EscapeDataString(token));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var error = await Assert.ThrowsAsync<RpcException>(() => ReadGrpc(fixture, new() {
            CanonicalName = "reports/A/snapshots/daily", PageSize = 1, PageToken = token,
        }, new Metadata { { "x-test-subject", subject }, { "x-test-tenant", tenant } }));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    private static async Task<ReadSnapshotGrpcResponse> ReadGrpc(SnapshotRelationFixture fixture, ReadSnapshotRequest request, Metadata? headers = null) {
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
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(headers), request);
        return await call.ResponseAsync;
    }

    private sealed class CallerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
            app.Use(async (context, continuation) => {
                context.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,
                    context.Request.Headers["X-Test-Subject"].ToString())], "test"));
                var tenant = Guid.TryParse(context.Request.Headers["X-Test-Tenant"], out var uid) ? uid : (Guid?)null;
                using var lease = TenantContext.Enter(new(tenant));
                await continuation();
            });
            next(app);
        };
    }
}
