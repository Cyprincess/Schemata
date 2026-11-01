using System;
using System.IO;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Flow.Skeleton.Runtime;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using FlowModels = Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Resource;

[Trait("Category", "Integration")]
[Collection("GrpcIntegration")]
public class FlowCustomMethodGrpcShould : IClassFixture<GrpcWebAppFactory>
{
    private readonly GrpcWebAppFactory _factory;

    public FlowCustomMethodGrpcShould(GrpcWebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Start_Exact_Version_After_Latest_Changes() {
        var registry = _factory.Services.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<ProcessVersionShould.Original>(configure: c => { c.Name = "grpc-version"; c.Version = "one"; });
        await registry.RegisterAsync<ProcessVersionShould.Replacement>(configure: c => { c.Name = "grpc-version"; c.Version = "two"; c.IsLatest = true; });
        var process = await Call<SchemataProcess, StartProcessInstanceRequest, SchemataProcess>(
            "start", new() { DefinitionName = "grpc-version", DefinitionVersion = "one" });
        Assert.Equal("one", process.DefinitionVersion);
        var completed = await Call<SchemataProcess, FlowModels.CompleteActivityRequest, ProcessSnapshot>(
            "complete", new() { CanonicalName = process.CanonicalName });
        Assert.Equal("Completed", completed.Process.State);
    }

    [Fact]
    public async Task StartProcess_Unknown_Definition_Returns_NotFound() {
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataProcess, StartProcessInstanceRequest, SchemataProcess>(
                "start", new() { DefinitionName = "missing" }));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task CompleteProcess_Missing_Instance_Returns_NotFound() {
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataProcess, FlowModels.CompleteActivityRequest, ProcessSnapshot>(
                "complete", new() { CanonicalName = "processes/missing" }));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task CorrelateProcess_Missing_Instance_Returns_NotFound() {
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataProcess, FlowModels.CorrelateMessageRequest, ProcessSnapshot>(
                "correlate", new() { CanonicalName = "processes/missing", MessageName = "approved" }));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task SignalProcess_Empty_Broadcast_Returns_Response() {
        var response = await Call<SchemataProcess, FlowModels.ThrowSignalRequest, EmptyResourceResponse>(
            "signal", new() { SignalName = "approved" });

        // The broadcast custom method always answers the AIP-136 empty envelope: the response
        // materializes, but carries no resource state on the wire.
        Assert.NotNull(response);
        Assert.Null(((ICanonicalName)response).Name);
        Assert.Null(((ICanonicalName)response).CanonicalName);
    }

    [Fact]
    public async Task TerminateProcess_Missing_Instance_Returns_NotFound() {
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataProcess, TerminateProcessResourceRequest, ProcessSnapshot>(
                "terminate", new() { CanonicalName = "processes/missing" }));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task CancelToken_Missing_Instance_Returns_NotFound() {
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataProcessToken, CancelTokenResourceRequest, ProcessSnapshot>(
                "cancel", new() { CanonicalName = "processes/missing/tokens/missing" }));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task Roundtrip_Flow_Entities_Through_The_Configured_Proto_Model() {
        var registry = _factory.Services.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<ProcessVersionShould.Original>(configure: configuration => configuration.Name = "grpc-internal-metadata");
        var process = await Call<SchemataProcess, StartProcessInstanceRequest, SchemataProcess>(
            "start", new() { DefinitionName = "grpc-internal-metadata" });
        Assert.Equal("grpc-internal-metadata", process.DefinitionName);
        Assert.NotNull(process.CanonicalName);

        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        object[] rows = [
            new SchemataProcess { Name = "process", CanonicalName = "processes/process", DefinitionName = "wire", DefinitionVersion = "one" },
            new SchemataProcessToken { Name = "token", CanonicalName = "processes/process/tokens/token", Process = "process", ScopeName = string.Empty, StateName = "task" },
            new SchemataProcessTransition { Name = "transition", CanonicalName = "processes/process/transitions/transition", Process = "process", Event = "advance" },
            new SchemataProcessParticipant { Process = "processes/process", Subject = "users/actor" },
            new SchemataProcessSource { Process = "processes/process", Name = "source", SourceType = "Order", Source = "orders/order" },
            new SchemataProcessCompensation { Name = "compensation", Process = "processes/process", ScopeOwnerCanonicalName = "processes/process/tokens/token", ActivityName = "undo", BoundaryName = "boundary" },
            new SchemataFlowEffectIntent { RequestId = "processes/process/tokens/token/effects/0", Process = "processes/process", Token = "processes/process/tokens/token", Task = "task" },
        ];
        foreach (var row in rows) {
            var type = row.GetType();
            SchemataProtoModelConfigurator.ConfigureType(model, type);
            using var stream = new MemoryStream();
            model.Serialize(stream, row);
            stream.Position = 0;
            var restored = model.Deserialize(stream, null, type);
            var property = type.GetProperty(type == typeof(SchemataProcess) ? nameof(SchemataProcess.DefinitionName) : nameof(SchemataProcessToken.Process))!;
            Assert.Equal(property.GetValue(row), property.GetValue(restored));
        }
    }

    private async Task<TResponse> Call<TEntity, TRequest, TResponse>(string verb, TRequest request)
        where TEntity : class
        where TRequest : class
        where TResponse : class {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(TRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(TResponse));
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var method = new Method<TRequest, TResponse>(
            MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(TEntity)),
            GrpcResourceNaming.CustomMethodName(descriptor, verb),
            GrpcMarshallers.Create<TRequest>(model),
            GrpcMarshallers.Create<TResponse>(model));
        using var call = _factory.CreateGrpcChannel().CreateCallInvoker().AsyncUnaryCall(method, null, new(), request);
        return await call.ResponseAsync;
    }
}
