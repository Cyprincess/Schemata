using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Grpc;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Xunit;
using global::Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Common;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Flow.Integration.Tests.Resource;

[Collection("GrpcIntegration")]
public sealed class ParticipantAccessShould : IClassFixture<ParticipantWebAppFactory>
{
    private readonly ParticipantWebAppFactory _factory;
    public ParticipantAccessShould(ParticipantWebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Filter_Reads_Before_Pagination_And_Require_Current_Eligibility_For_Raw_Commands() {
        var subject = "users/participant";
        var admin = Principal("users/admin", "start", "administer");
        SchemataProcess allowed;
        SchemataProcess hidden;
        string token;
        using (var scope = _factory.Services.CreateScope()) {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IProcessRegistry>().RegisterAsync<ProcessVersionShould.Replacement>(configure: c => c.Name = "participants");
            var runner = services.GetRequiredService<FlowRunner>();
            hidden = await runner.StartAsync("participants", null, admin, default);
            allowed = await runner.StartAsync("participants", null, admin, default);
            token = (await services.GetRequiredService<IRepository<SchemataProcessToken>>().FirstOrDefaultAsync(q => q.Where(t => t.Process == allowed.Name)))!.CanonicalName!;
            await services.GetRequiredService<FlowParticipantManager>().GrantAsync(allowed.CanonicalName!, subject, ProcessParticipationKind.Participation, admin);
        }
        using var client = Client(subject);
        var page = await client.GetAsync("/v1/processes?page_size=1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var process = Assert.Single(json.RootElement.GetProperty("processes").EnumerateArray());
        Assert.Equal(allowed.CanonicalName, process.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/" + allowed.CanonicalName)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/v1/" + hidden.CanonicalName)).StatusCode);
        using (var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpClient = client })) {
            var clients = ClientFactory.Create(_factory.Services.GetRequiredService<BinderConfiguration>());
            var grpc = channel.CreateGrpcService<IResourceService<SchemataProcess, SchemataProcess, SchemataProcess, SchemataProcess>>(clients);
            var listed = await grpc.ListAsync(new() { PageSize = 1 });
            Assert.Equal(allowed.CanonicalName, Assert.Single(listed.Entities!).CanonicalName);
            Assert.Equal(1, listed.TotalSize);
            Assert.Equal(allowed.Uid, (await grpc.GetAsync(new() { CanonicalName = allowed.CanonicalName })).Uid);
            var denied = await Assert.ThrowsAsync<global::Grpc.Core.RpcException>(() => grpc.GetAsync(new() { CanonicalName = hidden.CanonicalName }).AsTask());
            Assert.Equal(global::Grpc.Core.StatusCode.PermissionDenied, denied.StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync("/v1/students")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Permissions", "schemata-process.complete");
        var deniedMutation = await client.PostAsJsonAsync("/v1/" + allowed.CanonicalName + ":complete", new Skeleton.Models.CompleteActivityRequest { Token = token });
        Assert.Equal(HttpStatusCode.Forbidden, deniedMutation.StatusCode);
        using (var scope = _factory.Services.CreateScope()) {
            var services = scope.ServiceProvider;
            var dispatcher = services.GetRequiredService<IRequestDispatcher>();
            var actor = Principal(subject, "complete");
            await Assert.ThrowsAsync<PermissionDeniedException>(() => dispatcher.SendAsync<Foundation.Commands.CompleteActivityRequest, ProcessSnapshot>(new(allowed.CanonicalName!, token, actor)));
            await services.GetRequiredService<FlowParticipantManager>().GrantAsync(allowed.CanonicalName!, subject, ProcessParticipationKind.Eligibility, admin, token);
            await Assert.ThrowsAsync<PermissionDeniedException>(() => dispatcher.SendAsync<Foundation.Commands.CompleteActivityRequest, ProcessSnapshot>(new(allowed.CanonicalName!, token, Principal(subject))));
            var result = await dispatcher.SendAsync<Foundation.Commands.CompleteActivityRequest, ProcessSnapshot>(new(allowed.CanonicalName!, token, actor));
            Assert.Equal("Second", Assert.Single(result.Tokens).StateName);
            await Assert.ThrowsAsync<PermissionDeniedException>(() => dispatcher.SendAsync<Foundation.Commands.CompleteActivityRequest, ProcessSnapshot>(new(allowed.CanonicalName!, token, actor)));
            await Assert.ThrowsAsync<PermissionDeniedException>(() => dispatcher.SendAsync<Foundation.Commands.CompleteActivityRequest, ProcessSnapshot>(new(allowed.CanonicalName!, token, null)));
            var administrative = await dispatcher.SendAsync<TerminateProcessRequest, ProcessSnapshot>(new(allowed.CanonicalName!, Principal("users/admin", "terminate", "administer")));
            Assert.Equal("Terminated", administrative.Process.State);
        }
    }

    [Fact]
    public async Task Reject_Expired_Revoked_And_Foreign_Tenant_Participation() {
        var admin = Principal("users/manager", "start", "administer");
        SchemataProcess process;
        using (var scope = _factory.Services.CreateScope()) {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IProcessRegistry>().RegisterAsync<ProcessVersionShould.Original>(configure: c => c.Name = "invalid-participants");
            process = await services.GetRequiredService<FlowRunner>().StartAsync("invalid-participants", null, admin, default);
            var manager = services.GetRequiredService<FlowParticipantManager>();
            await manager.GrantAsync(process.CanonicalName!, "users/expired", ProcessParticipationKind.Participation, admin, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
            var revoked = await manager.GrantAsync(process.CanonicalName!, "users/revoked", ProcessParticipationKind.Participation, admin);
            await manager.RevokeAsync(revoked.Uid, admin);
            var rows = services.GetRequiredService<IRepository<SchemataProcessParticipant>>();
            await rows.AddAsync(new() { Process = process.CanonicalName!, Subject = "users/foreign", TenantUid = Guid.NewGuid(), Kind = ProcessParticipationKind.Participation });
            await rows.CommitAsync();
        }
        foreach (var subject in new[] { "users/expired", "users/revoked", "users/foreign" }) {
            using var client = Client(subject);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/v1/" + process.CanonicalName)).StatusCode);
        }
    }

    [Fact]
    public async Task Enforce_Eligibility_On_Grpc_Mutation_Then_Commit_Allowed_Transition() {
        var actor = "users/grpc-actor";
        var admin = Principal("users/grpc-admin", "start", "administer");
        SchemataProcess process;
        string token;
        using (var scope = _factory.Services.CreateScope()) {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IProcessRegistry>().RegisterAsync<ProcessVersionShould.Original>(configure: c => c.Name = "grpc-participant");
            process = await services.GetRequiredService<FlowRunner>().StartAsync("grpc-participant", null, admin, default);
            token = (await services.GetRequiredService<IRepository<SchemataProcessToken>>().FirstOrDefaultAsync(q => q.Where(t => t.Process == process.Name)))!.CanonicalName!;
        }
        using var client = Client(actor);
        client.DefaultRequestHeaders.Add("X-Permissions", "schemata-process.complete");
        using var channel = GrpcChannel.ForAddress("http://localhost", new() { HttpClient = client });
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(Skeleton.Models.CompleteActivityRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ProcessSnapshot));
        var method = new Method<Skeleton.Models.CompleteActivityRequest, ProcessSnapshot>(MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(SchemataProcess)),
            GrpcResourceNaming.CustomMethodName(ResourceNameDescriptor.ForType<SchemataProcess>(), "complete"),
            GrpcMarshallers.Create<Skeleton.Models.CompleteActivityRequest>(model), GrpcMarshallers.Create<ProcessSnapshot>(model));
        async Task<ProcessSnapshot> Complete() {
            using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(), new Skeleton.Models.CompleteActivityRequest { CanonicalName = process.CanonicalName, Token = token });
            return await call.ResponseAsync;
        }
        var denied = await Assert.ThrowsAsync<RpcException>(Complete);
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
        using (var scope = _factory.Services.CreateScope()) {
            await scope.ServiceProvider.GetRequiredService<FlowParticipantManager>().GrantAsync(process.CanonicalName!, actor, ProcessParticipationKind.Eligibility, admin, token);
        }
        Assert.Equal("Completed", (await Complete()).Process.State);
    }

    private HttpClient Client(string subject) {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Subject", subject);
        return client;
    }

    private static ClaimsPrincipal Principal(string subject, params string[] operations) => new(new ClaimsIdentity(
        new[] { new Claim("sub", subject) }.Concat(operations.Select(op => new Claim("permission", "schemata-process." + op))), ParticipantAuthentication.SchemeName));
}
