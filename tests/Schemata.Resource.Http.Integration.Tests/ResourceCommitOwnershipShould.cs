using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mapster;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

/// <summary>
///     The standard-method handler is the single commit owner: a detail-mapper failure after the
///     mutation must roll the dispatch back instead of leaving a committed delete or undelete.
/// </summary>
[Trait("Category", "Integration")]
public class ResourceCommitOwnershipShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceCommitOwnershipShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Delete_WhenTombstoneMappingFails_LeavesTheRowLive() {
        var faults = new MapperFault();
        using var host   = HostWithFaultingMapper(faults);
        using var client = host.CreateClient();
        var       name   = await CreateTrashAsync(client, "rollback-delete");

        faults.FailDetail = true;
        var response = await client.DeleteAsync($"/v1/{name}");

        Assert.True((int)response.StatusCode >= 500,
            $"A failed tombstone projection must surface as a server error, not {response.StatusCode}.");
        var row = await FindTrashAsync(host, name, includeDeleted: true);
        Assert.NotNull(row);
        Assert.Null(row.DeleteTime);
    }

    [Fact]
    public async Task Undelete_WhenDetailMappingFails_LeavesTheRowDeleted() {
        var faults = new MapperFault();
        using var host   = HostWithFaultingMapper(faults);
        using var client = host.CreateClient();
        var       name   = await CreateTrashAsync(client, "rollback-undelete");
        var       delete = await client.DeleteAsync($"/v1/{name}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.NotNull((await FindTrashAsync(host, name, includeDeleted: true))?.DeleteTime);

        faults.FailDetail = true;
        var response = await client.PostAsync(
            $"/v1/{name}:undelete", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.True((int)response.StatusCode >= 500,
            $"A failed restored-detail projection must surface as a server error, not {response.StatusCode}.");
        var row = await FindTrashAsync(host, name, includeDeleted: true);
        Assert.NotNull(row);
        Assert.NotNull(row.DeleteTime);
    }

    private WebApplicationFactory<Program> HostWithFaultingMapper(MapperFault fault) {
        return _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.AddSingleton(fault);
            services.RemoveAll<ISimpleMapper>();
            services.AddScoped<ISimpleMapper>(sp => new FaultingMapper(
                new Schemata.Mapping.Mapster.SimpleMapper(sp.GetRequiredService<TypeAdapterConfig>()),
                sp.GetRequiredService<MapperFault>()));
        }));
    }

    private static async Task<string> CreateTrashAsync(HttpClient client, string fullName) {
        var response = await client.PostAsJsonAsync("/v1/trashes", new Trash { FullName = fullName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var name = body.GetProperty("name").GetString();
        Assert.NotNull(name);
        return name;
    }

    private static async Task<Trash?> FindTrashAsync(
        WebApplicationFactory<Program> host,
        string                         canonicalName,
        bool                           includeDeleted
    ) {
        using var scope      = host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Trash>>();
        return await repository.FirstOrDefaultAsync(
            query => query.Where(row => row.CanonicalName == canonicalName),
            CancellationToken.None,
            new QueryOptions(IgnoreSoftDelete: includeDeleted));
    }

    private sealed class MapperFault
    {
        public bool FailDetail { get; set; }
    }

    private sealed class FaultingMapper(ISimpleMapper inner, MapperFault fault) : ISimpleMapper
    {
        public T? Map<T>(object source) { return inner.Map<T>(source); }

        public T? Map<T>(object source, Type sourceType, Type destinationType) {
            return inner.Map<T>(source, sourceType, destinationType);
        }

        public TDestination? Map<TSource, TDestination>(TSource source) {
            if (fault.FailDetail && typeof(TSource) == typeof(Trash) && typeof(TDestination) == typeof(Trash)) {
                throw new InvalidOperationException("detail mapping failed");
            }

            return inner.Map<TSource, TDestination>(source);
        }

        public void Map<TSource, TDestination>(TSource source, TDestination destination) {
            inner.Map(source, destination);
        }

        public void Map<TSource, TDestination>(TSource source, TDestination destination, IEnumerable<string> fields) {
            inner.Map(source, destination, fields);
        }
    }
}
