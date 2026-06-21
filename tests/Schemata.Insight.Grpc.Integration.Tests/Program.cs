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
using Schemata.Insight.Grpc.Integration.Tests.Fixtures;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

var dbName = "insight-grpc-integration-" + Identifiers.NewUid();

builder.UseSchemata(schema => {
    var insight = schema.UseInsight(i => {
        i.WithTotalSize(TotalSizeMode.Exact);
        i.AddRepositorySource("buyers", "buyers")
         .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });
    insight.UseAip().UseCel().UseOrdering();
    insight.MapGrpc();

    schema.Services.AddDbContextFactory<TestDbContext>(opts => opts.UseInMemoryDatabase(dbName));
    schema.Services.AddRepository<Buyer, EfCoreRepository<TestDbContext, Buyer>>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
    using var context = factory.CreateDbContext();
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
