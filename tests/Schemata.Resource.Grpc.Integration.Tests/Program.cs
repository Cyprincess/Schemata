using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;

namespace Schemata.Resource.Grpc.Integration.Tests;

public class Program
{
    public static void Main(string[] args) {
        
        var options = new WebApplicationOptions { Args = args };

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

            schema.Services.TryAddScoped<IRepository<Student>, EntityFrameworkCoreRepository<TestDbContext, Student>>();

            // Register repository advisors that normally come with AddRepository()
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQuerySoftDelete<>)));
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddConcurrency<>)));
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddSoftDelete<>)));
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddCanonicalName<>)));
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateTimestamp<>)));

            // Auto-assign a unique slug to every new Student (runs before AdviceAddCanonicalName).
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
        });

        var app = builder.Build();

        app.Run();
    }
}