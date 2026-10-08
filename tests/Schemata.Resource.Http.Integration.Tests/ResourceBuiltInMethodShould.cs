using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class ResourceBuiltInMethodShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceBuiltInMethodShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Undelete_Live_Resource_Returns_Conflict_Without_Changing_Row() {
        var name = await CreateTrashAsync("live-undelete");
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/v1/{name}:undelete", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var row = await FindTrashAsync(name);
        Assert.NotNull(row);
        Assert.Null(row.DeleteTime);
    }

    [Fact]
    public async Task Expunge_Live_Resource_Returns_BadRequest() {
        var name = await CreateTrashAsync("live-expunge");

        var response = await _factory.CreateClient().PostAsync(
            $"/v1/{name}:expunge", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Undelete_And_Expunge_Missing_Resources_Return_NotFound() {
        var client = _factory.CreateClient();

        var undelete = await client.PostAsync(
            "/v1/trashes/missing:undelete", new StringContent("{}", Encoding.UTF8, "application/json"));
        var expunge = await client.PostAsync(
            "/v1/trashes/missing:expunge", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, undelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, expunge.StatusCode);
    }

    [Fact]
    public async Task Purge_Preview_Returns_Pending_Operation_And_Stages_Request_Arguments() {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/v1/trashes:purge", new PurgeResourceRequest<Trash> { Filter = "*", Language = "aip", Force = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operation = await response.Content.ReadFromJsonAsync<Operation>(SchemataJson.Default);
        Assert.NotNull(operation);
        Assert.False(operation.Done);

        using var scope = _factory.Services.CreateScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        // Select the execution this dispatch staged by its operation identity: the shared fixture
        // accumulates purge executions from every test in the class.
        var execution = await executions.FirstOrDefaultAsync(
            query => query.Where(row => row.CanonicalName == (operation.Name ?? operation.CanonicalName)));
        Assert.NotNull(execution);
        Assert.NotNull(execution.ArgsJson);
        var args = JsonSerializer.Deserialize<PurgeOperationArgs>(execution.ArgsJson, SchemataJson.Default);
        Assert.NotNull(args);
        Assert.Equal("*", args.Filter);
        Assert.Equal("aip", args.Language);
        Assert.False(args.Force);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Purge_Force_Removes_All_SoftDeleted_Rows_And_Keeps_Live_Rows() {
        var doomed1 = await CreateTrashAsync($"purge-doomed-{Guid.NewGuid():n}");
        var doomed2 = await CreateTrashAsync($"purge-doomed-{Guid.NewGuid():n}");
        var spared  = await CreateTrashAsync($"purge-spared-{Guid.NewGuid():n}");
        var client  = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/v1/{doomed1}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/v1/{doomed2}")).StatusCode);

        var response = await client.PostAsJsonAsync(
            "/v1/trashes:purge", new PurgeResourceRequest<Trash> { Filter = "*", Language = "aip", Force = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The hosted job dispatcher executes the staged purge; poll for the physical outcome.
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Trash>>();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true) {
            using (repository.SuppressQuerySoftDelete()) {
                var remaining = await repository.LongCountAsync(
                    query => query.Where(row => row.CanonicalName == doomed1 || row.CanonicalName == doomed2));
                if (remaining == 0) {
                    break;
                }
            }
            if (DateTime.UtcNow >= deadline) {
                throw new TimeoutException("The purge job did not remove the soft-deleted rows within 30 seconds.");
            }
            await Task.Delay(100);
        }

        using (repository.SuppressQuerySoftDelete()) {
            var live = await repository.FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == spared));
            Assert.NotNull(live);
            Assert.Null(live.DeleteTime);
        }
    }

    private async Task<string> CreateTrashAsync(string fullName) {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/v1/trashes", new Trash { FullName = fullName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var name = body.GetProperty("name").GetString();
        Assert.NotNull(name);
        return name;
    }

    private async Task<Trash?> FindTrashAsync(string canonicalName) {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Trash>>();
        return await repository.FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == canonicalName));
    }
}
