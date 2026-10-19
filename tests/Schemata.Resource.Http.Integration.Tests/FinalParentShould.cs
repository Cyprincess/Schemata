using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Grpc;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class FinalParentShould
{
    [Theory]
    [InlineData("dispatcher")]
    [InlineData("http")]
    [InlineData("grpc")]
    public async Task Create_WithSeparateDto_AuthorizesOnlyThePersistedParent(string transport) {
        using var baseline = new WebAppFactory();
        using var host = baseline.WithWebHostBuilder(builder => builder.UseSetting("ParentGrpc", transport == "grpc" ? "true" : "false"));
        using var http = host.CreateClient(new() { BaseAddress = new("http://localhost") });
        if (transport == "http") {
            using var denied = await http.PostAsJsonAsync("/v1/parents/denied/records", new { parent = "parents/allowed" });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var allowed = await http.PostAsJsonAsync("/v1/parents/allowed/records", new { parent = "parents/denied" });
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        } else if (transport == "grpc") {
            using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
            var client = channel.CreateGrpcService<IResourceService<ParentedRecord, ParentedRecordRequest, ParentedRecord, ParentedRecord>>(
                ClientFactory.Create(host.Services.GetRequiredService<BinderConfiguration>()));
            var denied = await Assert.ThrowsAsync<RpcException>(async () => await client.CreateAsync(new() { Parent = "parents/denied" }));
            Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
            var allowed = await client.CreateAsync(new() { Parent = "parents/allowed" });
            Assert.Equal("allowed", allowed.ParentId);
        } else {
            using var scope = host.Services.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
            await Assert.ThrowsAsync<PermissionDeniedException>(() => dispatcher.SendAsync<CreateResourceRequest<ParentedRecord, ParentedRecordRequest, ParentedRecord>, CreateResultBase<ParentedRecord>>(
                new(new() { Parent = "parents/denied" }, null)));
            var allowed = await dispatcher.SendAsync<CreateResourceRequest<ParentedRecord, ParentedRecordRequest, ParentedRecord>, CreateResultBase<ParentedRecord>>(
                new(new() { Parent = "parents/allowed" }, null));
            Assert.Equal("allowed", allowed.Detail!.ParentId);
        }
        using var verification = host.Services.CreateScope();
        await using var database = await verification.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync();
        var persisted = Assert.Single(await database.ParentedRecords.ToListAsync());
        Assert.Equal("allowed", persisted.ParentId);
        Assert.StartsWith("parents/allowed/records/", persisted.CanonicalName);
    }
}
