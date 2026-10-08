using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Order;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);
var connectionString = $"Data Source=resource-grpc-{Guid.NewGuid():n};Mode=Memory;Cache=Shared;Default Timeout=30";
using var connection = new SqliteConnection(connectionString);
connection.Open();

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();
    schema.UseMapster().Map<Trash, Trash>();
    schema.UseMapster().Map<LockedStudent, LockedStudent>();
    schema.UseScheduling().MapGrpc();
    schema.UseFlow().MapGrpc();

    var resource = schema.UseResource();
    resource.UseAip().UseOrdering();
    if (builder.Configuration["ResourceSnapshot"] == "true") {
        resource.MapGrpc();
        var endpoints = new[] { GrpcResourceAttribute.Name };
        ResourceAttribute? input = null;
        resource.Use<Student, Student, Student, Student>(endpoints, registration => input = registration);
        var operations = builder.Configuration["ResourceSnapshotEmpty"] == "true"
            ? Array.Empty<Operations>() : new[] { Operations.Create, Operations.List, Operations.Get };
        schema.UseResource().Use<Student, Student, Student, Student>(endpoints, registration => {
            registration.AuthenticationScheme = "SnapshotScheme";
            registration.DefaultPageSize = 2;
            registration.MaxPageSize = 3;
            registration.TotalSize = TotalSizeMode.None;
            registration.Operations = operations;
        });
        endpoints[0] = "Disabled";
        if (operations.Length > 0) operations[0] = Operations.Delete;
        input!.Endpoints = ["Disabled"];
        input.AuthenticationScheme = "MutatedScheme";
        input.DefaultPageSize = 99;
        input.MaxPageSize = 99;
        input.Operations = [];
        schema.Services.AddSingleton(input);
    } else {
        resource.MapGrpc().Use<Student, Student, Student, Student>();
    }
    resource.MapGrpc().Use<Trash, Trash, Trash, Trash>();
    resource.MapGrpc().Use<LockedStudent, LockedStudent, LockedStudent, LockedStudent>();

    // Disable validation so freshness behavior remains isolated.
    resource.WithoutCreateValidation().WithoutUpdateValidation();

    schema.UseAuthentication(auth => auth.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.TestAuthScheme, null));
    schema.UseAuthentication(auth => auth.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>("SnapshotScheme", null));

    schema.Services.AddMemoryCacheProvider();

    schema.Services.AddDbContextFactory<TestDbContext>(opts => opts.UseSqlite(connectionString)
                                                                   .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

    schema.Services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();
    schema.Services.AddRepository<Trash, EfCoreRepository<TestDbContext, Trash>>();
    schema.Services.AddRepository<LockedStudent, EfCoreRepository<TestDbContext, LockedStudent>>();
    schema.Services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();
    schema.Services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
    schema.Services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
    schema.Services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
    schema.Services.AddRepository<SchemataProcessSource, EfCoreRepository<TestDbContext, SchemataProcessSource>>();
    schema.Services.AddRepository<SchemataProcessCompensation, EfCoreRepository<TestDbContext, SchemataProcessCompensation>>();
    schema.Services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
    schema.Services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
    schema.Services.AddScheduledJob<ProbeJob>();
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Trash>, AdviceAddTrashName>());

    // Supply the leaf name before canonical-name advice builds students/{slug}.
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcess>, AdviceAddResourceName<SchemataProcess>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcessToken>, AdviceAddResourceName<SchemataProcessToken>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcessTransition>, AdviceAddResourceName<SchemataProcessTransition>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcessCompensation>, AdviceAddResourceName<SchemataProcessCompensation>>());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
    database.Database.EnsureCreated();
}

app.Run();

namespace Schemata.Resource.Grpc.Integration.Tests
{
    public partial class Program;
}
