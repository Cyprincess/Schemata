using System;
using System.Threading;
using ProtoBuf;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Meta;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Xunit;
using static Schemata.Transport.Grpc.Proto.SchemataProtoModelConfigurator;

namespace Schemata.Flow.Integration.Tests.Resource;

[Collection("GrpcIntegration")]
public sealed class StreamGrpcTransportShould : IClassFixture<GrpcWebAppFactory>
{
    private static readonly Method<StreamProbeRequest, StreamProbeItem> Probe = CreateMethod();

    private readonly GrpcWebAppFactory _factory;
    public StreamGrpcTransportShould(GrpcWebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Deliver_Items_Incrementally_And_Release_Scope_On_Cancellation() {
        var state = _factory.Services.GetRequiredService<StreamProbeState>();
        var id = Guid.NewGuid().ToString("n");
        using var call = _factory.CreateGrpcChannel().CreateCallInvoker()
            .AsyncServerStreamingCall(Probe, null, new(), new() { Id = id });
        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.Equal(1, call.ResponseStream.Current.Value);
        Assert.False(state.End(id).Task.IsCompleted);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var move = call.ResponseStream.MoveNext(cancellation.Token);
        Assert.False(move.IsCompleted);
        call.Dispose();
        await state.End(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        await state.ScopeEnd(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.ScopeDisposals[id]);
        await Assert.ThrowsAsync<RpcException>(async () => await move);
    }

    [Fact]
    public async Task Complete_Both_Items_When_The_Producer_Finishes() {
        var state = _factory.Services.GetRequiredService<StreamProbeState>();
        var id = Guid.NewGuid().ToString("n");
        using var call = _factory.CreateGrpcChannel().CreateCallInvoker()
            .AsyncServerStreamingCall(Probe, null, new(), new() { Id = id });
        await call.ResponseStream.MoveNext(CancellationToken.None);
        state.Gate(id).TrySetResult();
        await call.ResponseStream.MoveNext(CancellationToken.None);
        Assert.Equal(2, call.ResponseStream.Current.Value);
        Assert.False(await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.True(state.End(id).Task.IsCompleted);
        Assert.Equal(StatusCode.OK, call.GetStatus().StatusCode);
        await state.ScopeEnd(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.ScopeDisposals[id]);
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preserve_Failure_Status_And_Dispose_Scope_Once(bool afterOutput) {
        var state = _factory.Services.GetRequiredService<StreamProbeState>();
        var id = Guid.NewGuid().ToString("n");
        using var channel = _factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncServerStreamingCall(Probe, null, new(),
            new() { Id = id, FailBefore = !afterOutput, FailAfter = afterOutput });
        if (afterOutput) {
            Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
            Assert.Equal(1, call.ResponseStream.Current.Value);
            state.Gate(id).TrySetResult();
        }
        var error = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal((int)StatusCode.InvalidArgument, Google.Rpc.Status.Parser.ParseFrom(error.Trailers.GetValueBytes("grpc-status-details-bin")).Code);
        await state.ScopeEnd(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, state.ScopeDisposals[id]);
    }

    private static Method<StreamProbeRequest, StreamProbeItem> CreateMethod() {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        ConfigureType(model, typeof(StreamProbeRequest));
        ConfigureType(model, typeof(StreamProbeItem));
        return new(MethodType.ServerStreaming, "schemata.StreamService", "Probe",
            GrpcMarshallers.Create<StreamProbeRequest>(model), GrpcMarshallers.Create<StreamProbeItem>(model));
    }
}
