using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Grpc.Integration.Tests;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();

    var resource = schema.UseResource();
    resource.MapGrpc().Use<Student, Student, Student, Student>();

    // Suppress validation noise; keep freshness enabled for ETag tests
    resource.WithoutCreateValidation().WithoutUpdateValidation();

    schema.Services.AddDistributedMemoryCache();
    schema.Services.AddDistributedCache();

    var dbName = "grpc-integration-" + Identifiers.NewUid();
    schema.Services.AddDbContextFactory<TestDbContext>(
        opts => opts.UseInMemoryDatabase(dbName));

    schema.Services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();

    // Auto-assign a unique slug to every new Student (runs before AdviceAddCanonicalName).
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
});

var app = builder.Build();

app.Run();

public partial class Program;
