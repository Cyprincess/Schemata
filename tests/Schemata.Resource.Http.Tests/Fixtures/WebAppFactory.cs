using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Schemata.Resource.Http.Tests.Fixtures;

public class WebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Testing");

        // Override content root to the actual test project directory.
        // WebApplication.CreateBuilder resolves content root from the app assembly name;
        // since the project is under tests/ the default path is wrong.
        var contentRoot = FindProjectRoot();
        if (contentRoot is not null) {
            builder.UseContentRoot(contentRoot);
        }
    }

    private static string? FindProjectRoot() {
        // Walk up from current directory to find the project directory
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "Schemata.Resource.Http.Tests.csproj"))) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        // Fallback: look for known relative path from repo root
        var candidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "tests",
                                                      "Schemata.Resource.Http.Tests"));
        return Directory.Exists(candidate) ? candidate : null;
    }
}
