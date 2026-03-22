using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Http.Tests.Fixtures;

// When the content root cannot be resolved from the current directory
// (as happens when running under WebApplicationFactory from the test output folder),
// fall back to a well-known path so WebApplication.CreateBuilder does not throw.
var options = new WebApplicationOptions { Args = args, ContentRootPath = ResolveContentRoot() };

var builder = WebApplication.CreateBuilder(options);

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();

    var resource = schema.UseResource();
    resource.MapHttp().Use<Student, Student, Student, Student>();
    resource.WithoutCreateValidation().WithoutUpdateValidation().WithoutFreshness();

    schema.Services.AddDistributedMemoryCache();

    var dbName = "integration-" + Guid.NewGuid();
    schema.Services.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName));

    schema.Services.AddScoped<IRepository<Student>, EntityFrameworkCoreRepository<TestDbContext, Student>>();

    // Auto-assign a unique Name to every new Student so that FindByNameAsync works.
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
});

var app = builder.Build();

app.Run();

static string ResolveContentRoot() {
    // Walk up from current working directory to find the test project root.
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null) {
        if (File.Exists(Path.Combine(dir.FullName, "Schemata.Resource.Http.Tests.csproj"))) {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    // When running from the build output (artifacts/bin/...) walk up to repo root.
    // The test project is at <repo root>/tests/Schemata.Resource.Http.Tests.
    var current = Directory.GetCurrentDirectory();
    var candidate = Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..",
                                                  "tests", "Schemata.Resource.Http.Tests"));
    if (Directory.Exists(candidate)) {
        return candidate;
    }

    // Last resort: use the current directory (will work if dotnet test sets cwd correctly)
    return current;
}

public partial class Program
{ }

/// <summary>
///     Assigns a GUID-based canonical name to every new Student.
/// </summary>
internal sealed class StudentNameAdvisor : IRepositoryAddAdvisor<Student>
{
    #region IRepositoryAddAdvisor<Student> Members

    public int Order    => 0;
    public int Priority => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<Student> repository,
        Student              entity,
        CancellationToken    ct
    ) {
        if (string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = $"student-{Guid.NewGuid():N}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
