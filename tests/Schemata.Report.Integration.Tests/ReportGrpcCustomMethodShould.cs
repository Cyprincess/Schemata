using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton.Models;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton.Entities;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public class ReportGrpcCustomMethodShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ReportGrpcCustomMethodShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Generate_Uses_Custom_Grpc_Method() {
        var operation = await Call<SchemataReport, GenerateReportRequest, Operation>(
            "generate", new() { Name = "dsl-records", Persist = true, Sync = true });

        Assert.True(operation.Done);
        var response = operation.Response;
        Assert.NotNull(response);
        var payload = response.Output;
        Assert.NotNull(payload);
        var output = JsonSerializer.Deserialize<ReportOperationOutput>(payload, SchemataJson.Default);
        Assert.NotNull(output);
        Assert.False(string.IsNullOrWhiteSpace(output.Snapshot));
    }

    [Fact]
    public async Task ReadSnapshot_Current_Dictionary_Row_Wire_Returns_Unknown() {
        var operation = await Call<SchemataReport, GenerateReportRequest, Operation>(
            "generate", new() { Name = "dsl-records", Persist = true, Sync = true });
        var operationResponse = operation.Response;
        Assert.NotNull(operationResponse);
        var snapshotPayload = operationResponse.Output;
        Assert.NotNull(snapshotPayload);
        var output = JsonSerializer.Deserialize<ReportOperationOutput>(snapshotPayload, SchemataJson.Default);
        Assert.NotNull(output);
        Assert.False(string.IsNullOrWhiteSpace(output.Snapshot));
        var request = new ReadSnapshotRequest { CanonicalName = output.Snapshot, PageSize = 2 };

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotResponse>("read", request));

        Assert.Equal(StatusCode.Unknown, error.StatusCode);
        Assert.Equal("Exception was thrown by handler.", error.Status.Detail);
    }

    [Fact]
    public async Task Generate_With_Name_And_Query_Returns_InvalidArgument() {
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataReport, GenerateReportRequest, Operation>(
                "generate", new() { Name = "dsl-records", Query = new(), Sync = true }));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
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
        using var channel = _factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(), request);
        return await call.ResponseAsync;
    }
}
