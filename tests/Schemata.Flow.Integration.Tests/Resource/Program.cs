using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Order;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Integration.Tests.Resource;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;
using Schemata.Scheduling.Skeleton.Entities;

var options = new WebApplicationOptions { Args = args };
var builder = WebApplication.CreateBuilder(options);

var connectionString = $"Data Source=flow-resource-{Guid.NewGuid():n};Mode=Memory;Cache=Shared;Default Timeout=30";
using var connection = new SqliteConnection(connectionString);
connection.Open();

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();
    schema.UseMapster().Map<Trash, Trash>();
    schema.UseFlow().MapHttp().MapGrpc();
    schema.UseScheduling().MapHttp().MapGrpc();

    var resource = schema.UseResource();
    resource.UseAip().UseOrdering();
    resource.MapHttp().Use<Student, Student, Student, Student>();
    resource.MapHttp().Use<Trash, Trash, Trash, Trash>();
    resource.MapGrpc().Use<Student, Student, Student, Student>();
    resource.MapGrpc().Use<Trash, Trash, Trash, Trash>();
    resource.WithoutCreateValidation().WithoutUpdateValidation().WithoutFreshness();

    schema.Services.AddDistributedMemoryCache();
    schema.Services.AddDistributedCache();

    schema.Services.AddDbContextFactory<TestDbContext>(opts => opts.UseSqlite(connectionString)
                                                                   .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
    schema.Services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();
    schema.Services.AddRepository<Trash, EfCoreRepository<TestDbContext, Trash>>();
    schema.Services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();
    schema.Services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
    schema.Services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
    schema.Services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
    schema.Services.AddRepository<SchemataProcessSource, EfCoreRepository<TestDbContext, SchemataProcessSource>>();
    schema.Services.AddRepository<SchemataProcessCompensation, EfCoreRepository<TestDbContext, SchemataProcessCompensation>>();
    schema.Services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
    schema.Services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
    schema.Services.AddScheduledJob<ProbeJob>();

    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Trash>, AdviceAddTrashName>());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
    database.Database.EnsureCreated();
}

app.Run();

namespace Schemata.Flow.Integration.Tests.Resource
{
    public partial class Program;
}
