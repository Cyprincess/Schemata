using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Integration.Tests.Fixtures;

public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configure;
    private readonly string _environment;

    public WebAppFactory() : this("Testing", null) { }

    private WebAppFactory(string environment, Action<IServiceCollection>? configure) {
        _environment = environment;
        _configure = configure;
    }

    public WebAppFactory WithEnvironment(string environment) { return new(environment, _configure); }

    public WebAppFactory WithServices(Action<IServiceCollection> configure) { return new(_environment, configure); }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment(_environment);
        builder.UseContentRoot(GetProjectDirectory());
        if (_configure is not null) {
            builder.ConfigureServices(_configure);
        }
    }

    private static string GetProjectDirectory() {
        var dir = Path.GetDirectoryName(typeof(WebAppFactory).Assembly.Location)!;
        while (!File.Exists(Path.Combine(dir, "Schemata.Authorization.Integration.Tests.csproj"))
            && dir != Path.GetPathRoot(dir)) {
            dir = Path.GetDirectoryName(dir)!;
        }

        return dir;
    }
}
