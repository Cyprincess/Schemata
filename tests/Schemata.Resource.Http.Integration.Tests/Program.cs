using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Xunit;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Order;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton.Entities;

var options = new WebApplicationOptions { Args = args };
var builder = WebApplication.CreateBuilder(options);

var connectionString = $"Data Source=resource-http-{Guid.NewGuid():n};Mode=Memory;Cache=Shared;Default Timeout=30";
using var connection = new SqliteConnection(connectionString);
connection.Open();

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();
    schema.UseMapster().Map<Trash, Trash>();
    schema.UseMapster().Map<LockedStudent, LockedStudent>();
    schema.UseMapster().Map<PagedThing, PagedThing>();
    schema.UseMapster().Map<ParentedRecordRequest, ParentedRecord>();
    schema.UseMapster().Map<ParentedRecord, ParentedRecord>();
    schema.UseMapster().Map<IdempotentOrderRequest, IdempotentOrder>();
    schema.UseFlow().MapHttp();
    schema.UseScheduling().MapHttp();

    var resource = schema.UseResource();
    if (builder.Configuration["ResourceExclusionsFirst"] == "true") {
        resource.WithoutCreateValidation().WithoutUpdateValidation().WithoutFreshness();
    }
    resource.UseAip().UseOrdering();
    if (builder.Configuration["ResourceDeclarations"] == "true") {
        resource.MapHttp().MapGrpc();
        schema.UseMapster().Map<HttpNote, HttpNote>();
        schema.UseMapster().Map<GrpcNote, GrpcNote>();
        if (builder.Configuration["ResourceDeclarationsEmpty"] == "true") {
            resource.AddResource<HttpNote>([]);
        } else if (builder.Configuration["ResourceDeclarationsConfigureEmpty"] == "true") {
            resource.AddResource<HttpNote>(configure: registration => registration.Endpoints = []);
        } else {
            resource.AddResource<HttpNote>();
        }
        resource.AddResource<GrpcNote>();
    }
    if (builder.Configuration["ResourceSnapshot"] == "true") {
        resource.MapHttp().MapGrpc();
        var first = builder.Configuration["ResourceSnapshotGrpcFirst"] == "true" ? GrpcResourceAttribute.Name : HttpResourceAttribute.Name;
        var second = first == HttpResourceAttribute.Name ? GrpcResourceAttribute.Name : HttpResourceAttribute.Name;
        var endpoints = new[] { first };
        ResourceAttribute? input = null;
        var method = new ResourceMethodAttribute("inspect", typeof(PreviewHandler)) { Method = ResourceHttpMethod.Get };
        resource.Use<Student, Student, Student, Student>(endpoints, registration => {
            input = registration;
            registration.Methods = [method];
        });
        var operations = builder.Configuration["ResourceSnapshotEmpty"] == "true"
            ? Array.Empty<Operations>() : new[] { Operations.Create, Operations.List, Operations.Get };
        var supplementEndpoints = new[] { second };
        ResourceAttribute? supplement = null;
        schema.UseResource().Use<Student, Student, Student, Student>(supplementEndpoints, registration => {
            supplement = registration;
            registration.AuthenticationScheme = "SnapshotScheme";
            registration.DefaultPageSize = 2;
            registration.MaxPageSize = 3;
            registration.TotalSize = TotalSizeMode.None;
            registration.Operations = operations;
        });
        resource.Use<Student, Student, Student, Student>(supplementEndpoints, registration => {
            registration.AuthenticationScheme = "SnapshotScheme";
            registration.DefaultPageSize = 2;
            registration.MaxPageSize = 3;
            registration.TotalSize = TotalSizeMode.None;
            registration.Operations = operations;
        });
        switch (builder.Configuration["ResourceSnapshotConflict"]) {
            case "tuple":
                Assert.Throws<InvalidOperationException>(() => resource.Use<Student, LockedStudent, Student, Student>());
                break;
            case "scheme":
                Assert.Throws<InvalidOperationException>(() => resource.Use<Student, Student, Student, Student>(null,
                    registration => registration.AuthenticationScheme = "ConflictingScheme"));
                break;
            case "page":
                Assert.Throws<InvalidOperationException>(() => resource.Use<Student, Student, Student, Student>(null,
                    registration => registration.DefaultPageSize = 4));
                break;
            case "operations":
                Assert.Throws<InvalidOperationException>(() => resource.Use<Student, Student, Student, Student>(null,
                    registration => registration.Operations = [Operations.Delete]));
                break;
            case "method":
                Assert.Throws<InvalidOperationException>(() => resource.Use<Student, Student, Student, Student>(null,
                    registration => registration.Methods = [new("inspect", typeof(PreviewHandler))]));
                break;
        }
        endpoints[0] = "Disabled";
        supplementEndpoints[0] = "Disabled";
        supplement!.AuthenticationScheme = "MutatedSupplementScheme";
        supplement.DefaultPageSize = 98;
        supplement.MaxPageSize = 98;
        supplement.TotalSize = TotalSizeMode.Exact;
        if (operations.Length > 0) operations[0] = Operations.Delete;
        input!.Endpoints = ["Disabled"];
        input.AuthenticationScheme = "MutatedScheme";
        input.DefaultPageSize = 99;
        input.MaxPageSize = 99;
        input.Operations = [];
        method.Method = ResourceHttpMethod.Post;
        schema.Services.AddSingleton(input);
    } else {
        resource.MapHttp().Use<Student, Student, Student, Student>();
    }
    resource.MapHttp().Use<Trash, Trash, Trash, Trash>();
    resource.MapHttp().Use<LockedStudent, LockedStudent, LockedStudent, LockedStudent>();
    resource.MapHttp().Use<PagedThing, PagedThing, PagedThing, PagedThing>();
    resource.MapHttp().Use<ParentedRecord, ParentedRecordRequest, ParentedRecord, ParentedRecord>();
    resource.MapHttp().Use<IdempotentOrder, IdempotentOrderRequest, IdempotentOrder, IdempotentOrder>();
    if (builder.Configuration["ParentGrpc"] == "true") {
        resource.MapGrpc().Use<ParentedRecord, ParentedRecordRequest, ParentedRecord, ParentedRecord>();
    }
    schema.Services.AddSingleton<Schemata.Security.Skeleton.IAccessProvider<ParentedRecord, ParentedRecordRequest>, ParentedRecordAccess>();
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<Schemata.Resource.Foundation.Advisors.IResourceCreateAdvisor<ParentedRecord, ParentedRecordRequest>, Schemata.Resource.Foundation.Advisors.ResourceCreateAccessAdvisor<ParentedRecord, ParentedRecordRequest>>());
    if (builder.Configuration["ResourceExclusionsFirst"] != "true") {
        resource.WithoutCreateValidation().WithoutUpdateValidation().WithoutFreshness();
    }
    if (builder.Configuration["ResourceRepeatInstall"] == "true") {
        schema.UseResource();
    }

    schema.UseAuthentication(auth => auth.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.TestScheme, null));
    schema.UseAuthentication(auth => auth.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>("SnapshotScheme", null));

    schema.Services.AddMemoryCacheProvider();
    schema.Services.AddTenantCache();
    schema.Services.AddSchemataSchedulingRepositoryStore();

    schema.Services.AddDbContextFactory<TestDbContext>(opts => opts.UseSqlite(connectionString)
                                                                   .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
    schema.Services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();
    schema.Services.AddRepository<Trash, EfCoreRepository<TestDbContext, Trash>>();
    schema.Services.AddRepository<LockedStudent, EfCoreRepository<TestDbContext, LockedStudent>>();
    schema.Services.AddRepository<HttpNote, EfCoreRepository<TestDbContext, HttpNote>>();
    schema.Services.AddRepository<GrpcNote, EfCoreRepository<TestDbContext, GrpcNote>>();
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<HttpNote>, AdviceAddResourceName<HttpNote>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<GrpcNote>, AdviceAddResourceName<GrpcNote>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<PagedThing>, AdviceAddResourceName<PagedThing>>());
    schema.Services.AddRepository<PagedThing, EfCoreRepository<TestDbContext, PagedThing>>();
    schema.Services.AddRepository<ParentedRecord, EfCoreRepository<TestDbContext, ParentedRecord>>();
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<ParentedRecord>, AdviceAddResourceName<ParentedRecord>>());
    schema.Services.AddRepository<IdempotentOrder, EfCoreRepository<TestDbContext, IdempotentOrder>>();
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<IdempotentOrder>, AdviceAddResourceName<IdempotentOrder>>());
    schema.Services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();
    schema.Services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
    schema.Services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
    schema.Services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
    schema.Services.AddRepository<SchemataProcessSource, EfCoreRepository<TestDbContext, SchemataProcessSource>>();
    schema.Services.AddRepository<SchemataProcessCompensation, EfCoreRepository<TestDbContext, SchemataProcessCompensation>>();
    schema.Services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
    schema.Services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
    schema.Services.AddScheduledJob<ProbeJob>();

    // Auto-assign a unique Name to every new Student so that FindByNameAsync works.
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Trash>, AdviceAddTrashName>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcess>, AdviceAddResourceName<SchemataProcess>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcessToken>, AdviceAddResourceName<SchemataProcessToken>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcessTransition>, AdviceAddResourceName<SchemataProcessTransition>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcessCompensation>, AdviceAddResourceName<SchemataProcessCompensation>>());
});

builder.Services.Insert(0, ServiceDescriptor.Transient<Microsoft.AspNetCore.Hosting.IStartupFilter, TenantHeaderStartupFilter>());

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
    database.Database.EnsureCreated();
}

app.Run();

namespace Schemata.Resource.Http.Integration.Tests
{
    public partial class Program;
}
