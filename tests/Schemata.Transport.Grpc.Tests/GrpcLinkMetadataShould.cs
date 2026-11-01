using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Abstractions.Linking;
using Schemata.Common.Linking;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Transport.Grpc.Linking;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Transport.Grpc.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class GrpcLinkMetadataShould
{
    [Fact]
    public async Task PaginatedResponse_CarriesNextLinkMetadata() {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = "Testing",
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
            listen => listen.Protocols = HttpProtocols.Http2));

        var connectionString = $"Data Source=grpc-links-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        builder.UseSchemata(schema => {
            schema.UseMapster().Map<LinkEntry, LinkEntry>();
            schema.UseResource().MapGrpc().Use<LinkEntry, LinkEntry, LinkEntry, LinkEntry>(
                null, resource => resource.Operations = [Operations.List]);
            schema.Services.AddDbContextFactory<LinkDbContext>(options =>
                options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            schema.Services.AddRepository<LinkEntry, EfCoreRepository<LinkDbContext, LinkEntry>>();
        });

        await using var app = builder.Build();
        using (var scope = app.Services.CreateScope()) {
            var database = scope.ServiceProvider.GetRequiredService<LinkDbContext>();
            await database.Database.EnsureCreatedAsync();
            database.LinkEntries.AddRange(
                new LinkEntry { Uid = Guid.NewGuid(), Name = "one", CanonicalName = "linkEntries/one" },
                new LinkEntry { Uid = Guid.NewGuid(), Name = "two", CanonicalName = "linkEntries/two" });
            await database.SaveChangesAsync();
        }

        await app.StartAsync();
        try {
            var address = app.Services.GetRequiredService<IServer>().Features
                             .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var channel = GrpcChannel.ForAddress(address,
                new() { HttpHandler = new SocketsHttpHandler { UseProxy = false } });
            var invoker = channel.CreateCallInvoker();
            var service = await ReflectionClient.Discover(invoker,
                typeof(LinkEntry).Namespace + ".LinkEntryService");
            var list = service.Methods.Single(method => method.Name == "ListLinkEntries");
            var rpc  = ReflectionClient.Method(list);

            using var first = invoker.AsyncUnaryCall(rpc, null, new CallOptions(),
                ReflectionClient.Encode(list.InputType, new Dictionary<string, object?> { ["page_size"] = 1 }));
            var firstPage = ReflectionClient.Decode(list.OutputType, await first.ResponseAsync);
            Assert.Equal(StatusCode.OK, first.GetStatus().StatusCode);
            var token     = Assert.Single(firstPage["next_page_token"]).ToString();
            Assert.False(string.IsNullOrEmpty(token));

            var link = Assert.Single(LinkHeader.Parse(Trailer(first.GetTrailers())));
            Assert.Equal("next", link.Rel);
            Assert.Equal(token, Assert.Single(link.Extensions!).Value);
            Assert.Equal($"/{service.FullName}/{list.Name}", link.Target);

            using var second = invoker.AsyncUnaryCall(rpc, null, new CallOptions(),
                ReflectionClient.Encode(list.InputType,
                    new Dictionary<string, object?> { ["page_size"] = 1, ["page_token"] = token }));
            Assert.NotEmpty(await second.ResponseAsync);
            Assert.Equal(StatusCode.OK, second.GetStatus().StatusCode);
        } finally {
            await app.StopAsync();
        }
    }

    private static string Trailer(Metadata trailers) =>
        string.Join(", ", trailers.Where(entry => entry.Key == GrpcLinkMetadataInterceptor.TrailerName)
                                  .Select(entry => entry.Value));

    [CanonicalName("linkEntries/{link_entry}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class LinkEntry : ICanonicalName, IIdentifier
    {
        public Guid Uid { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class LinkDbContext(DbContextOptions<LinkDbContext> options) : DbContext(options)
    {
        public DbSet<LinkEntry> LinkEntries { get; set; } = null!;
    }
}
