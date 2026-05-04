using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Http.Integration.Tests;
using Schemata.Resource.Http.Integration.Tests.Fixtures;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

builder.UseSchemata(schema => {
        schema.UseMapster().Map<Student, Student>();

        var resource = schema.UseResource();
        resource.MapHttp().Use<Student, Student, Student, Student>();
        resource.WithoutCreateValidation().WithoutUpdateValidation().WithoutFreshness();

        schema.Services.AddDistributedMemoryCache();
        schema.Services.AddDistributedCacheProvider();

        var dbName = "integration-" + Guid.NewGuid();
        schema.Services.AddDbContext<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName));

        schema.Services.AddRepository<Student, EntityFrameworkCoreRepository<TestDbContext, Student>>();

        // Auto-assign a unique Name to every new Student so that FindByNameAsync works.
        schema.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>()
        );
    }
);

var app = builder.Build();

app.Run();

public partial class Program;
