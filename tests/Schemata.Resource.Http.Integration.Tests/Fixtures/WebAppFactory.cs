
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

public class WebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
    }
}
