using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Schemata.Authorization.Integration.Tests.Fixtures;

public class WebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(GetProjectDirectory());
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
