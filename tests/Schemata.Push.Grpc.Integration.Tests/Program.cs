using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Push.Grpc.Integration.Tests.Fixtures;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });

var connectionString = $"Data Source=push-grpc-{Guid.NewGuid():n};Mode=Memory;Cache=Shared;Default Timeout=30";
using var connection = new SqliteConnection(connectionString);
connection.Open();

builder.Services.AddAuthentication("Test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, PushGrpcAuthenticationHandler>("Test", _ => { });
builder.Services.AddAuthorization(options => {
    options.AddPolicy("push.subscriptions.create", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "push.subscriptions.create"));
    options.AddPolicy("push.subscriptions.get",    policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "push.subscriptions.get"));
    options.AddPolicy("push.subscriptions.delete", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "push.subscriptions.delete"));
    options.AddPolicy("push.send",                 policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "push.send"));
});

builder.Services.AddDbContextFactory<PushGrpcDbContext>(options => options.UseSqlite(connectionString)
    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
builder.Services.AddRepository<SchemataPushSubscription, EfCoreRepository<PushGrpcDbContext, SchemataPushSubscription>>();

builder.Services.AddSchemataPush();
builder.Services.AddSchemataPushGrpc();
builder.Services.AddSchemataPushHttp();

builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<Schemata.Entity.Repository.Advisors.IRepositoryAddAdvisor<SchemataPushSubscription>, AdviceAddSubscriptionName>());
builder.Services.AddSingleton<IPushTransport>(new RecordingTransport("ok"));
builder.Services.AddSingleton<IPushTransport>(new RecordingTransport("later"));

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
    var database = scope.ServiceProvider.GetRequiredService<PushGrpcDbContext>();
    database.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseSchemataExceptionHandler();
app.UseAuthorization();
app.MapSchemataPushGrpc();
app.MapControllers();

app.Run();

namespace Schemata.Push.Grpc.Integration.Tests
{
    public partial class Program;
}
