using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Grpc.Integration.Tests.Fixtures;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();

var buyerOrders = new System.Collections.Generic.List<OrderQuery> {
    new() { Status = "paid", Amount = 100, Meta = new() { ["code"] = "Z" } },
    new() { Status = "paid", Amount = 200, Meta = new() { ["code"] = "A" } },
};
builder.UseSchemata(schema => {
    var insight = schema.UseInsight(i => {
        i.WithTotalSize(TotalSizeMode.Exact);
        if (builder.Environment.EnvironmentName == "Authenticated") {
            i.WithAuthentication("InsightTest");
        }

        i.AddRepositorySource<Buyer, BuyerQuery>("buyers", b => new BuyerQuery { Id = b.Id, FullName = b.FullName, Secret = b.FullName,
            Orders = buyerOrders })
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
        i.AddRepositorySource<Buyer, ValueQuery>("values", b => ValueQuery.From(b.Id));
    });
    insight.UseAip().UseCel().UseOrdering();
    insight.MapGrpc();
    insight.MapHttp();
    schema.UseAuthentication((Microsoft.AspNetCore.Authentication.AuthenticationBuilder _) => { });

    schema.Services.AddDbContextFactory<TestDbContext>(opts => opts.UseSqlite(connection));
    schema.Services.AddRepository<Buyer, EfCoreRepository<TestDbContext, Buyer>>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
    using var context = factory.CreateDbContext();
    context.Database.EnsureCreated();
    context.Buyers.AddRange(
        new Buyer { Uid = Guid.NewGuid(), Id = 1, FullName = "Ada" },
        new Buyer { Uid = Guid.NewGuid(), Id = 2, FullName = "Bob" });
    context.SaveChanges();
}

app.Run();

namespace Schemata.Insight.Grpc.Integration.Tests
{
    public partial class Program;
}
