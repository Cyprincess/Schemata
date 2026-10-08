using System;
using Schemata.Push.Http.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Push.Foundation;
using Schemata.Push.Http;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

var builder = WebApplication.CreateBuilder(args);
var connectionString = $"Data Source=push-http-{Guid.NewGuid():n};Mode=Memory;Cache=Shared";
using var connection = new SqliteConnection(connectionString);
connection.Open();

builder.Services.AddAuthentication("Test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, PushAuthenticationHandler>("Test", _ => { });
builder.Services.AddAuthorization(options => {
    foreach (var policy in new[] {
        "push.subscriptions.create", "push.subscriptions.get", "push.subscriptions.delete", "push.send",
    }) {
        options.AddPolicy(policy, policyBuilder => policyBuilder.RequireAuthenticatedUser().RequireClaim("permission", policy));
    }
});

builder.Services.AddDbContextFactory<PushDbContext>(options => options.UseSqlite(connectionString)
                                                            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
builder.Services.AddRepository<SchemataPushSubscription, EfCoreRepository<PushDbContext, SchemataPushSubscription>>();
builder.UseSchemata(schema => {
    schema.UseAuthentication((Microsoft.AspNetCore.Authentication.AuthenticationBuilder _) => { });
    schema.UsePush().MapHttp().MapHttp();
});
builder.Services.AddSchemataPushHttp();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<Schemata.Entity.Repository.Advisors.IRepositoryAddAdvisor<SchemataPushSubscription>, AdviceAddSubscriptionName>());
builder.Services.AddSingleton<IPushTransport>(new RecordingTransport("ok"));
builder.Services.AddSingleton<IPushTransport>(new RecordingTransport("later"));

var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    scope.ServiceProvider.GetRequiredService<PushDbContext>().Database.EnsureCreated();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

namespace Schemata.Push.Http.Integration.Tests
{
    public partial class Program;
}
