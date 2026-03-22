using System.IO;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Configuration;

namespace Schemata.Resource.Grpc.Tests.Fixtures;

public class WebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");

        var contentRoot = FindProjectRoot();
        if (contentRoot is not null) {
            builder.UseContentRoot(contentRoot);
        }
    }

    public GrpcChannel CreateGrpcChannel() {
        var client = CreateClient(new() { BaseAddress = new("http://localhost") });
        return GrpcChannel.ForAddress(client.BaseAddress!, new() { HttpClient = client });
    }

    /// <summary>
    ///     Creates a gRPC channel and a <see cref="ClientFactory" /> built from the server-side
    ///     <see cref="BinderConfiguration" />, so that <c>CreateGrpcService</c> uses the same
    ///     <see cref="ProtoBuf.Meta.RuntimeTypeModel" /> that was configured during startup.
    /// </summary>
    public (GrpcChannel Channel, ClientFactory Client) CreateGrpcChannelWithClient() {
        var channel = CreateGrpcChannel();
        var binder  = Services.GetRequiredService<BinderConfiguration>();
        return (channel, ClientFactory.Create(binder));
    }

    private static string? FindProjectRoot() {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "Schemata.Resource.Grpc.Tests.csproj"))) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        var candidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "tests",
                                                      "Schemata.Resource.Grpc.Tests"));
        return Directory.Exists(candidate) ? candidate : null;
    }
}
