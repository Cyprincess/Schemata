using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Http.Integration.Tests;
using Schemata.Resource.Http.Integration.Tests.Fixtures;

// When the content root cannot be resolved from the current directory
// (as happens when running under WebApplicationFactory from the test output folder),
// fall back to a well-known path so WebApplication.CreateBuilder does not throw.
var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();

    var resource = schema.UseResource();
    resource.MapHttp().Use<Student, Student, Student, Student>();
    resource.WithoutCreateValidation().WithoutUpdateValidation().WithoutFreshness();

    schema.Services.AddDistributedMemoryCache();

    var dbName = "integration-" + Guid.NewGuid();
    schema.Services.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName));

    schema.Services.TryAddScoped<IRepository<Student>, EntityFrameworkCoreRepository<TestDbContext, Student>>();

    // Auto-assign a unique Name to every new Student so that FindByNameAsync works.
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
});

var app = builder.Build();

app.Run();
