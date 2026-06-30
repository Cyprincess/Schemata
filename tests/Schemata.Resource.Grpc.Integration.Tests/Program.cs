using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Order;
using Schemata.Resource.Grpc.Integration.Tests;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();

    var resource = schema.UseResource();
    resource.UseAip().UseOrdering();
    resource.MapGrpc().Use<Student, Student, Student, Student>();

    // Disable validation so freshness behavior remains isolated.
    resource.WithoutCreateValidation().WithoutUpdateValidation();

    schema.Services.AddDistributedMemoryCache();
    schema.Services.AddDistributedCache();

    var dbName = "grpc-integration-" + Identifiers.NewUid();
    schema.Services.AddDbContextFactory<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName));

    schema.Services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();

    // Supply the leaf name before canonical-name advice builds students/{slug}.
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());
});

var app = builder.Build();

app.Run();

namespace Schemata.Resource.Grpc.Integration.Tests
{
    public partial class Program;
}
