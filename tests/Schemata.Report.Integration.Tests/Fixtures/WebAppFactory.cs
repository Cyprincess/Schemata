using System;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Report.Integration.Tests.Fixtures;

public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configure;
    private readonly string                      _environment;

    public WebAppFactory() : this("Development", null) { }

    internal WebAppFactory(Action<IServiceCollection> configure) : this("Development", configure) { }

    internal WebAppFactory(string environment) : this(environment, null) { }

    private WebAppFactory(string environment, Action<IServiceCollection>? configure) {
        _environment = environment;
        _configure   = configure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment(_environment);
        builder.UseContentRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        if (_configure is not null) {
            builder.ConfigureServices(_configure);
        }
    }
}
