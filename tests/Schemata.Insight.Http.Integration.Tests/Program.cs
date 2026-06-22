using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;
using Schemata.Insight.Http.Integration.Tests.Fixtures;
using Schemata.Insight.Skeleton;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

var dbName = "insight-integration-" + Identifiers.NewUid();

builder.UseSchemata(schema => {
    var insight = schema.UseInsight(i => {
        i.WithTotalSize(TotalSizeMode.Exact);
        i.AddRepositorySource("students", "students")
         .AddRepositorySource("customers", "customers")
         .AddRepositorySource("buyers", "buyers")
         .AddRepositorySource("purchases", "purchases")
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });
    insight.UseAip().UseCel().UseOrdering();
    insight.UseDatabaseCatalog();
    insight.MapHttp();

    schema.Services.AddDbContextFactory<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName));
    schema.Services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();
    schema.Services.AddRepository<Customer, EfCoreRepository<TestDbContext, Customer>>();
    schema.Services.AddRepository<Buyer, EfCoreRepository<TestDbContext, Buyer>>();
    schema.Services.AddRepository<Purchase, EfCoreRepository<TestDbContext, Purchase>>();
    schema.Services.AddRepository<SchemataInsightSource, EfCoreRepository<TestDbContext, SchemataInsightSource>>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
    using var context = factory.CreateDbContext();
    context.Students.AddRange(
        new Student { Uid = Guid.NewGuid(), Name = "ada", FullName = "Ada", Age = 36 },
        new Student { Uid = Guid.NewGuid(), Name = "bob", FullName = "Bob", Age = 19 },
        new Student { Uid = Guid.NewGuid(), Name = "cleo", FullName = "Cleo", Age = 24 });

    context.Customers.Add(new() {
        Uid      = Guid.NewGuid(),
        Name     = "ada",
        FullName = "Ada",
        Orders = [
            new() { Uid = Guid.NewGuid(), Number = 1, Status = "paid", Amount = 100, Placed = 3 },
            new() { Uid = Guid.NewGuid(), Number = 2, Status = "open", Amount = 999, Placed = 2 },
            new() { Uid = Guid.NewGuid(), Number = 3, Status = "paid", Amount = 200, Placed = 5 },
            new() { Uid = Guid.NewGuid(), Number = 4, Status = "paid", Amount = 50, Placed = 1 },
        ],
    });

    context.Buyers.AddRange(
        new Buyer { Uid = Guid.NewGuid(), Id = 1, FullName = "Ada" },
        new Buyer { Uid = Guid.NewGuid(), Id = 2, FullName = "Bob" });
    context.Purchases.AddRange(
        new Purchase { Uid = Guid.NewGuid(), BuyerId = 1, Amount = 100, Status = "paid" },
        new Purchase { Uid = Guid.NewGuid(), BuyerId = 1, Amount = 50, Status = "open" },
        new Purchase { Uid = Guid.NewGuid(), BuyerId = 2, Amount = 200, Status = "paid" });

    context.InsightSources.Add(new() {
        Uid    = Guid.NewGuid(),
        Name   = "live_buyers",
        Driver = "repository",
        Params = """{"resource":"buyers"}""",
    });

    context.SaveChanges();
}

app.Run();

namespace Schemata.Insight.Http.Integration.Tests
{
    public partial class Program;
}
