using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Schemata.Identity.Scim.Integration.Tests.Fixtures;

public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;

    public WebAppFactory(string environment = "Testing") {
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment(_environment);
        builder.UseContentRoot(Path.GetDirectoryName(typeof(WebAppFactory).Assembly.Location)!);
    }
}
