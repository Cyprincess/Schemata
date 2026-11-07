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
using System.Threading;
using Grpc.Core.Testing;
using Microsoft.AspNetCore.Http;
using Moq;
using Schemata.Core.Linking;

namespace Schemata.Transport.Grpc.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class GrpcLinkMetadataShould
{
    [Theory]
    [InlineData("title")]
    [InlineData("input")]
    public async Task Invalid_Ancillary_Metadata_Omits_Only_That_Link_And_Returns_Completed_Response(string invalid) {
        var valid = new DomainOperationTarget("valid");
        var bad = new DomainOperationTarget("bad");
        var contributor = new Mock<ILinkContributor>();
        contributor.Setup(value => value.GetLinks(It.IsAny<LinkContext>())).Returns(new[] {
            new SemanticLink("related", bad, invalid == "title" ? "consumer\nlabel" : null),
            new SemanticLink("self", valid),
        });
        var resolver = new Mock<IGrpcLinkTargetResolver>();
        resolver.Setup(value => value.Resolve(valid)).Returns(new GrpcLinkTarget("/service/Read", new Dictionary<string, string?> { ["name"] = "items/one" }));
        resolver.Setup(value => value.Resolve(bad)).Returns(new GrpcLinkTarget("/service/Read", new Dictionary<string, string?> {
            ["filter"] = invalid == "input" ? "value=one\n" : "value=one",
        }));
        using var services = new ServiceCollection().AddSingleton(resolver.Object)
            .AddSingleton<GrpcLinkProjector>().AddKeyedSingleton<ILinkContributor>(LinkTransports.Grpc, contributor.Object)
            .BuildServiceProvider();
        var context = TestServerCallContext.Create("/service/Read", "host", DateTime.MaxValue, new(),
            CancellationToken.None, "peer", null, null, _ => Task.CompletedTask, () => new(), _ => { });
        context.UserState["__HttpContext"] = new DefaultHttpContext { RequestServices = services };
        var completed = new object();
        var result = await new GrpcLinkMetadataInterceptor().UnaryServerHandler<object, object>(new(), context,
            (_, _) => Task.FromResult(completed));
        Assert.Same(completed, result);
        Assert.Equal(StatusCode.OK, context.Status.StatusCode);
        var link = Assert.Single(LinkHeader.Parse(Trailer(context.ResponseTrailers)));
        Assert.Equal("self", link.Rel);
        Assert.Equal("items/one", link.Extensions!["name"]);
    }

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

            var links = LinkHeader.Parse(Trailer(first.GetTrailers()));
            Assert.Contains(links, candidate => candidate.Rel == "self");
            var link = Assert.Single(links, candidate => candidate.Rel == "next");
            Assert.Equal(token, link.Extensions!["page_token"]);
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
