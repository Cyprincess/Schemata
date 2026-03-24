using System.Reflection;

// Tell WebApplicationFactory<Program> where the project root is.
// The path is relative to the test assembly's build output directory.
// Going up from artifacts/bin/Schemata.Resource.Http.Integration.Tests/Debug/net8.0
// to the repo root takes 5 levels, then we go into tests/Schemata.Resource.Http.Integration.Tests.
[assembly:
    AssemblyMetadata("Microsoft.AspNetCore.Testing.ApplicationRootPath",
                     "../../../../../tests/Schemata.Resource.Http.Integration.Tests")]
