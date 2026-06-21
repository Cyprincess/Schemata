using System.IO;
using System.Reflection;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Schemata.Insight.Grpc.Integration.Tests.Fixtures;

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
}
