using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class SchedulingCustomMethodShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public SchedulingCustomMethodShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task RunJob_Returns_Addressable_Operation() {
        var name = $"run-{Guid.NewGuid():n}";
        await SeedAsync(new SchemataJob {
            Name = name, CanonicalName = $"jobs/{name}", JobKey = ProbeJob.JobKey, State = JobState.Active,
        });

        var response = await _factory.CreateClient().PostAsJsonAsync($"/v1/jobs/{name}:run", new RunJobRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operation = await response.Content.ReadFromJsonAsync<Operation>(SchemataJson.Default);
        Assert.NotNull(operation);
        Assert.False(string.IsNullOrWhiteSpace(operation.Name ?? operation.CanonicalName));
    }

    [Fact]
    public async Task RunJob_Unrunnable_And_Missing_Return_Expected_Statuses() {
        var name = $"unrunnable-{Guid.NewGuid():n}";
        await SeedAsync(new SchemataJob {
            Name = name, CanonicalName = $"jobs/{name}", JobKey = "missing-job-type", State = JobState.Active,
        });
        var client = _factory.CreateClient();

        var unrunnable = await client.PostAsJsonAsync($"/v1/jobs/{name}:run", new RunJobRequest());
        var missing    = await client.PostAsJsonAsync("/v1/jobs/missing:run", new RunJobRequest());

        Assert.Equal(HttpStatusCode.PreconditionFailed, unrunnable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task CancelOperation_Uses_Route_Target_Over_Request_Payload() {
        var execution     = Execution(ExecutionState.Pending, DateTime.UtcNow.AddHours(1));
        var payloadTarget = Execution(ExecutionState.Pending, DateTime.UtcNow.AddHours(1));
        await SeedAsync(execution);
        await SeedAsync(payloadTarget);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/v1/operations/{execution.Name}:cancel",
            new CancelOperationRequest { CanonicalName = payloadTarget.CanonicalName });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operation = await response.Content.ReadFromJsonAsync<Operation>(SchemataJson.Default);
        Assert.NotNull(operation);
        Assert.Equal(execution.CanonicalName, operation.Name ?? operation.CanonicalName);
        Assert.True(operation.Done);
        Assert.Equal(1, operation.Error!.Code);
    }

    [Fact]
    public async Task CancelOperation_Terminal_And_Missing_Return_Expected_Statuses() {
        var execution = Execution(ExecutionState.Succeeded, DateTime.UtcNow.AddHours(-1));
        await SeedAsync(execution);
        var client = _factory.CreateClient();

        var terminal = await client.PostAsJsonAsync(
            $"/v1/operations/{execution.Name}:cancel", new CancelOperationRequest { CanonicalName = execution.Name });
        var missing = await client.PostAsJsonAsync(
            "/v1/operations/missing:cancel", new CancelOperationRequest { CanonicalName = "missing" });

        Assert.Equal(HttpStatusCode.PreconditionFailed, terminal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task WaitOperation_Returns_Terminal_And_Pending_Snapshots() {
        var terminal = Execution(ExecutionState.Succeeded, DateTime.UtcNow.AddHours(-1));
        terminal.EndTime = DateTime.UtcNow;
        var pending = Execution(ExecutionState.Pending, DateTime.UtcNow.AddHours(1));
        await SeedAsync(terminal);
        await SeedAsync(pending);
        var client = _factory.CreateClient();
        var request = new WaitOperationRequest { Timeout = TimeSpan.FromMilliseconds(50) };

        var doneResponse = await client.PostAsJsonAsync($"/v1/operations/{terminal.Name}:wait", request);
        var openResponse = await client.PostAsJsonAsync($"/v1/operations/{pending.Name}:wait", request);

        Assert.Equal(HttpStatusCode.OK, doneResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openResponse.StatusCode);
        Assert.True((await doneResponse.Content.ReadFromJsonAsync<Operation>(SchemataJson.Default))!.Done);
        Assert.False((await openResponse.Content.ReadFromJsonAsync<Operation>(SchemataJson.Default))!.Done);
    }

    [Fact]
    public async Task WaitOperation_Missing_Returns_NotFound() {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/v1/operations/missing:wait",
            new WaitOperationRequest { Timeout = TimeSpan.FromMilliseconds(10) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SeedAsync<TEntity>(TEntity entity) where TEntity : class {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
        await repository.AddAsync(entity);
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
}
