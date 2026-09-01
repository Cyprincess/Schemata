using System;
using System.IO;
using System.Reflection;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Insight.Grpc.Integration.Tests.Fixtures;

public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configure;
    private readonly string                      _environment;

    public WebAppFactory() : this("Testing", null) { }

    private WebAppFactory(string environment, Action<IServiceCollection>? configure) {
        _environment = environment;
        _configure   = configure;
    }

    public WebAppFactory WithAuthentication() { return new WebAppFactory("Authenticated", _configure); }

    public WebAppFactory WithServices(Action<IServiceCollection> configure) { return new WebAppFactory(_environment, configure); }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment(_environment);
        builder.UseContentRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        if (_configure is not null) {
            builder.ConfigureServices(_configure);
        }
    }

    public GrpcChannel CreateGrpcChannel() {
        var client = CreateClient(new() { BaseAddress = new("http://localhost") });
        return GrpcChannel.ForAddress(client.BaseAddress!, new() { HttpClient = client });
    }
}
