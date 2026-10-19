using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Http.Integration.Tests.Fixtures;
using Schemata.Insight.Skeleton.Entities;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();

builder.UseSchemata(schema => {
    var insight = schema.UseInsight(i => {
        i.WithTotalSize(TotalSizeMode.Exact);
        if (builder.Environment.EnvironmentName == "Authenticated") {
            i.WithAuthentication("InsightTest");
        }

        i.AddRepositorySource<Student, StudentQuery>("students", s => new StudentQuery { FullName = s.FullName, Age = s.Age })
         .AddRepositorySource<Customer, CustomerQuery>("customers", c => new CustomerQuery { FullName = c.FullName,
             Orders = c.Orders.Select(o => new OrderQuery { Number = o.Number, Status = o.Status, Amount = o.Amount, Placed = o.Placed }).ToList() })
         .AddRepositorySource<Buyer, BuyerQuery>("buyers", b => new BuyerQuery { Id = b.Id, FullName = b.FullName })
         .AddRepositorySource<Purchase, PurchaseQuery>("purchases", p => new PurchaseQuery { BuyerId = p.BuyerId, Amount = p.Amount, Status = p.Status })
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
        i.AddRepositorySource<Customer, ProtectedQuery>("protected", c => new ProtectedQuery {
            Label = c.FullName,
            Years = c.Orders.Count,
            Secret = c.Orders.Count,
            String = c.FullName,
            Children = c.Orders.OrderBy(o => o.Number).Select(o => new ProtectedChild {
                Number = o.Number, Secret = o.Status,
                Detail = new ProtectedDetail { Label = o.Status, Secret = o.Status },
            }).ToList(),
        });
    });
    insight.UseAip().UseCel().UseOrdering();
    insight.UseDatabaseCatalog();
    insight.MapHttp();
    schema.UseAuthentication((Microsoft.AspNetCore.Authentication.AuthenticationBuilder _) => { });

    schema.Services.AddDbContextFactory<TestDbContext>(opts => {
        opts.UseSqlite(connection);
        opts.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
    });
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
    context.Database.EnsureCreated();
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
        Params = """{"binding":"buyers"}""",
    });

    context.SaveChanges();
}

app.Run();

namespace Schemata.Insight.Http.Integration.Tests
{
    public partial class Program;
}
