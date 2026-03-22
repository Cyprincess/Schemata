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
using Schemata.Resource.Grpc.Tests.Fixtures;

// When the content root cannot be resolved from the current directory
// (as happens when running under WebApplicationFactory from the test output folder),
// fall back to a well-known path so WebApplication.CreateBuilder does not throw.
var options = new WebApplicationOptions { Args = args, ContentRootPath = ResolveContentRoot() };

var builder = WebApplication.CreateBuilder(options);

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();

    var resource = schema.UseResource();
    resource.MapGrpc().Use<Student, Student, Student, Student>();

    // Suppress validation noise; keep freshness enabled for ETag tests
    resource.WithoutCreateValidation().WithoutUpdateValidation();

    schema.Services.AddDistributedMemoryCache();

    var dbName = "grpc-integration-" + Guid.NewGuid();
    schema.Services.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName));

    schema.Services.AddScoped<IRepository<Student>, EntityFrameworkCoreRepository<TestDbContext, Student>>();

    // Register repository advisors that normally come with AddRepository()
    schema.Services.TryAddEnumerable(
        ServiceDescriptor.Scoped(typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQuerySoftDelete<>)));
    schema.Services.TryAddEnumerable(
        ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddConcurrency<>)));
    schema.Services.TryAddEnumerable(
        ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddSoftDelete<>)));
    schema.Services.TryAddEnumerable(
        ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddCanonicalName<>)));
    schema.Services.TryAddEnumerable(
        ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateTimestamp<>)));

    // Auto-assign a unique slug to every new Student (runs before AdviceAddCanonicalName).
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
});

var app = builder.Build();

app.Run();

static string ResolveContentRoot() {
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null) {
        if (File.Exists(Path.Combine(dir.FullName, "Schemata.Resource.Grpc.Tests.csproj"))) {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    var current = Directory.GetCurrentDirectory();
    var candidate = Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..",
                                                  "tests", "Schemata.Resource.Grpc.Tests"));
    if (Directory.Exists(candidate)) {
        return candidate;
    }

    return current;
}

public partial class Program
{ }

/// <summary>
///     Assigns a canonical name to every new Student so that GetByCanonicalNameAsync works.
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
            // Set just the leaf slug; AdviceAddCanonicalName will resolve the full
            // canonical name "students/{slug}" using the [CanonicalName] attribute.
            entity.Name = Guid.NewGuid().ToString("N");
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
