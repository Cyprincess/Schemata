using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Identity.Integration.Tests.Fixtures;

public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configure;
    private readonly string _environment;

    public WebAppFactory() : this("Testing", null) { }

    private WebAppFactory(string environment, Action<IServiceCollection>? configure) {
        _environment = environment;
        _configure = configure;
    }

    public WebAppFactory WithAuthentication() { return new("Authenticated", _configure); }

    public WebAppFactory WithServices(Action<IServiceCollection> configure) { return new(_environment, configure); }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment(_environment);
        builder.UseContentRoot(Path.GetDirectoryName(typeof(WebAppFactory).Assembly.Location)!);
        if (_configure is not null) builder.ConfigureServices(_configure);
    }
}
