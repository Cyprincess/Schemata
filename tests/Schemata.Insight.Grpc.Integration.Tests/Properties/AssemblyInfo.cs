using System.Reflection;

// Tell WebApplicationFactory<Program> where the project root is, relative to the test assembly's
// build output directory (five levels up from artifacts/bin/.../Debug/net8.0 to the repo root).
[assembly:
    AssemblyMetadata("Microsoft.AspNetCore.Testing.ApplicationRootPath",
                     "../../../../../tests/Schemata.Insight.Grpc.Integration.Tests")]
