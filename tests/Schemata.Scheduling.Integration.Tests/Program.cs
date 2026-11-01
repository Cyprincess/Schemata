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
using Schemata.Scheduling.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton.Entities;

var builder = WebApplication.CreateBuilder(args);

var connectionString = $"Data Source=scheduling-http-{Guid.NewGuid():n};Mode=Memory;Cache=Shared;Default Timeout=30";
using var connection = new SqliteConnection(connectionString);
connection.Open();

builder.UseSchemata(schema => {
    schema.UseMapster();
    schema.UseScheduling().MapHttp();

    var resource = schema.UseResource();
    resource.UseAip().UseOrdering();

    schema.Services.AddSchemataSchedulingRepositoryStore();
    schema.Services.AddDbContextFactory<SchedulingDbContext>(opts => opts.UseSqlite(connectionString)
                                                                       .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
    schema.Services.AddRepository<SchemataJob, EfCoreRepository<SchedulingDbContext, SchemataJob>>();
    schema.Services.AddRepository<SchemataJobExecution, EfCoreRepository<SchedulingDbContext, SchemataJobExecution>>();
    schema.Services.AddScoped<IUnitOfWork<SchedulingDbContext>, EfCoreUnitOfWork<SchedulingDbContext>>();
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    scope.ServiceProvider.GetRequiredService<SchedulingDbContext>().Database.EnsureCreated();
}

app.Run();

namespace Schemata.Scheduling.Integration.Tests
{
    public partial class Program;
}
