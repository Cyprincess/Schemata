using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Schemata.Resource.Grpc.Internal;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Trait("Category", "Integration")]
[Collection("GrpcIntegration")]
public class CustomMethodGrpcShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public CustomMethodGrpcShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task RunJob_Returns_Addressable_Operation() {
        var name = $"run-{Guid.NewGuid():n}";
        await SeedAsync(new SchemataJob {
            Name = name, CanonicalName = $"jobs/{name}", JobKey = ProbeJob.JobKey, State = JobState.Active,
        });

        var operation = await Call<SchemataJob, RunJobRequest, Operation>(
            "run", new() { CanonicalName = $"jobs/{name}" });

        Assert.False(string.IsNullOrWhiteSpace(operation.Name ?? operation.CanonicalName));
    }

    [Fact]
    public async Task RunJob_Missing_Returns_NotFound() {
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataJob, RunJobRequest, Operation>("run", new() { CanonicalName = "jobs/missing" }));

        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task CancelOperation_And_WaitOperation_Return_Expected_Envelopes() {
        var pending = Execution(ExecutionState.Pending, DateTime.UtcNow.AddHours(1));
        var done    = Execution(ExecutionState.Succeeded, DateTime.UtcNow.AddHours(-1));
        done.EndTime = DateTime.UtcNow;
        await SeedAsync(pending);
        await SeedAsync(done);

        var cancelled = await Call<SchemataJobExecution, CancelOperationRequest, Operation>(
            "cancel", new() { CanonicalName = pending.CanonicalName });
        var waited = await Call<SchemataJobExecution, WaitOperationRequest, Operation>(
            "wait", new() { CanonicalName = done.CanonicalName, Timeout = TimeSpan.FromMilliseconds(50) });

        Assert.True(cancelled.Done);
        Assert.Equal(1, cancelled.Error!.Code);
        Assert.True(waited.Done);
    }

    [Fact]
    public async Task Operation_Methods_Missing_Return_NotFound() {
        var cancel = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataJobExecution, CancelOperationRequest, Operation>(
                "cancel", new() { CanonicalName = "operations/missing" }));
        var wait = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataJobExecution, WaitOperationRequest, Operation>(
                "wait", new() { CanonicalName = "operations/missing", Timeout = TimeSpan.FromMilliseconds(10) }));

        Assert.Equal(StatusCode.NotFound, cancel.StatusCode);
        Assert.Equal(StatusCode.NotFound, wait.StatusCode);
    }

    [Fact]
    public async Task Undelete_And_Expunge_Invoke_BuiltIn_Grpc_Methods() {
        var deleted = TrashRow(deleted: true);
        var live    = TrashRow(deleted: false);
        await SeedTrashAsync(deleted);
        await SeedAsync(live);

        var restored = await Call<Trash, UndeleteResourceRequest<Trash, Trash>, Trash>(
            "undelete", new() { CanonicalName = deleted.CanonicalName });
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            Call<Trash, ExpungeResourceRequest<Trash>, EmptyResourceResponse>(
                "expunge", new() { CanonicalName = live.CanonicalName }));
        Assert.Null(restored.DeleteTime);
        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
    }

    [Fact]
    public async Task Undelete_Live_Returns_AlreadyExists_And_Missing_Returns_NotFound() {
        var live = TrashRow(deleted: false);
        await SeedAsync(live);

        var exists = await Assert.ThrowsAsync<RpcException>(() =>
            Call<Trash, UndeleteResourceRequest<Trash, Trash>, Trash>(
                "undelete", new() { CanonicalName = live.CanonicalName }));
        var missing = await Assert.ThrowsAsync<RpcException>(() =>
            Call<Trash, UndeleteResourceRequest<Trash, Trash>, Trash>(
                "undelete", new() { CanonicalName = "trashes/missing" }));
        Assert.Equal(StatusCode.AlreadyExists, exists.StatusCode);
        Assert.Equal(StatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Purge_Preview_Returns_Operation_And_Stages_Request() {
        var operation = await Call<Trash, PurgeResourceRequest<Trash>, Operation>(
            "purge", new() { Filter = "*", Language = "aip", Force = false });
        using var scope = _factory.Services.CreateScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var execution = await executions.FirstOrDefaultAsync(query => query.Where(row => row.Method == "purge"));
        Assert.NotNull(execution);
        Assert.Equal(execution.CanonicalName, operation.Name ?? operation.CanonicalName);
        var args = System.Text.Json.JsonSerializer.Deserialize<PurgeOperationArgs>(execution.ArgsJson!, SchemataJson.Default);
        Assert.NotNull(args);
        Assert.False(args.Force);
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

    private async Task SeedAsync<TEntity>(TEntity entity) where TEntity : class {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
        await repository.AddAsync(entity);
        await repository.CommitAsync();
    }

    private async Task SeedTrashAsync(Trash row) {
        await SeedAsync(row);
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Trash>>();
        row.DeleteTime = DateTime.UtcNow;
        await repository.UpdateAsync(row);
        await repository.CommitAsync();
    }

    private static SchemataJobExecution Execution(ExecutionState state, DateTime start) {
        var uid  = Guid.NewGuid();
        var name = uid.ToString("n");
        return new() {
            Uid = uid, Name = name, CanonicalName = $"operations/{name}", State = state,
            StartTime = start, Method = "test",
        };
    }

    private static Trash TrashRow(bool deleted) {
        var uid  = Guid.NewGuid();
        var name = $"trash-{uid:n}";
        return new() {
            Uid = uid, Name = name, CanonicalName = $"trashes/{name}", Timestamp = Guid.NewGuid(),
            DeleteTime = deleted ? DateTime.UtcNow : null,
        };
    }
}
