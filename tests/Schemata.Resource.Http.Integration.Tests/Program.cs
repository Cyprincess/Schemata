using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Order;
using Schemata.Resource.Http.Integration.Tests;
using Schemata.Resource.Http.Integration.Tests.Fixtures;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

builder.UseSchemata(schema => {
    schema.UseMapster().Map<Student, Student>();
    schema.UseMapster().Map<Trash, Trash>();

    var resource = schema.UseResource();
    resource.UseAip().UseOrdering();
    resource.MapHttp().Use<Student, Student, Student, Student>();
    resource.MapHttp().Use<Trash, Trash, Trash, Trash>();
    resource.WithoutCreateValidation().WithoutUpdateValidation().WithoutFreshness();

    schema.Services.AddDistributedMemoryCache();
    schema.Services.AddDistributedCache();

    var dbName = "integration-" + Identifiers.NewUid();
    schema.Services.AddDbContextFactory<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName));

    schema.Services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();
    schema.Services.AddRepository<Trash, EfCoreRepository<TestDbContext, Trash>>();

    // Auto-assign a unique Name to every new Student so that FindByNameAsync works.
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Trash>, AdviceAddTrashName>());
});

var app = builder.Build();

app.Run();

namespace Schemata.Resource.Http.Integration.Tests
{
    public partial class Program;
}
