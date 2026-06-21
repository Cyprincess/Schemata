using System.Reflection;

// Tell WebApplicationFactory<Program> where the project root is.
// The path is relative to the test assembly's build output directory:
// artifacts/bin/Schemata.Insight.Http.Integration.Tests/Debug/net8.0 to the repo root
// is five levels up, then into the test project directory.
[assembly:
    AssemblyMetadata("Microsoft.AspNetCore.Testing.ApplicationRootPath",
                     "../../../../../tests/Schemata.Insight.Http.Integration.Tests")]
