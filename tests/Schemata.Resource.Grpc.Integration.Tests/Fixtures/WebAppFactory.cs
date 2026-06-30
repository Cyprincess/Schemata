using System.IO;
using System.Reflection;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Configuration;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

public class WebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
    }

    public GrpcChannel CreateGrpcChannel() {
        var client = CreateClient(new() { BaseAddress = new("http://localhost") });
        return GrpcChannel.ForAddress(client.BaseAddress!, new() { HttpClient = client });
    }

    public (GrpcChannel Channel, ClientFactory Client) CreateGrpcChannelWithClient() {
        var channel = CreateGrpcChannel();
        var binder  = Services.GetRequiredService<BinderConfiguration>();
        return (channel, ClientFactory.Create(binder));
    }
}
