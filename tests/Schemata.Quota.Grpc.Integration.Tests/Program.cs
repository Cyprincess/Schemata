using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Quota.Grpc.Integration.Tests;
using Schemata.Quota.Grpc.Integration.Tests.Fixtures;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Control;
using Schemata.Quota.Skeleton.Entities;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
var connectionString = $"Data Source=quota-grpc-{Guid.NewGuid():n};Mode=Memory;Cache=Shared;Default Timeout=30";
using var connection = new SqliteConnection(connectionString);
connection.Open();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthentication(QuotaGrpcAuthenticationHandler.SchemeName)
       .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, QuotaGrpcAuthenticationHandler>(
           QuotaGrpcAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options => {
    options.AddPolicy(QuotaPolicies.Reserve, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", QuotaPolicies.Reserve));
    options.AddPolicy(QuotaPolicies.Commit, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", QuotaPolicies.Commit));
    options.AddPolicy(QuotaPolicies.Release, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", QuotaPolicies.Release));
    options.AddPolicy(QuotaPolicies.Ledger, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", QuotaPolicies.Ledger));
});
if (builder.Environment.EnvironmentName != "MissingScope") {
    builder.Services.AddSingleton<IQuotaScopeResolver>(new StaticQuotaScopeResolver(new("scope")));
}

builder.Services.AddDbContextFactory<QuotaTransportDbContext>(options => options.UseSqlite(connectionString)
    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
builder.Services.AddRepository<SchemataQuotaAccount, EfCoreRepository<QuotaTransportDbContext, SchemataQuotaAccount>>();
builder.Services.AddRepository<SchemataQuotaSegment, EfCoreRepository<QuotaTransportDbContext, SchemataQuotaSegment>>();
builder.Services.AddRepository<SchemataQuotaReservation, EfCoreRepository<QuotaTransportDbContext, SchemataQuotaReservation>>();
builder.Services.AddRepository<SchemataQuotaReservationItem, EfCoreRepository<QuotaTransportDbContext, SchemataQuotaReservationItem>>();
builder.Services.AddRepository<SchemataQuotaLedgerEntry, EfCoreRepository<QuotaTransportDbContext, SchemataQuotaLedgerEntry>>();
builder.Services.AddSchemataQuotaAccounting();
builder.Services.AddSchemataQuotaHttp();
builder.Services.AddSchemataQuotaGrpc();

var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<QuotaTransportDbContext>>();
    await using var database = await factory.CreateDbContextAsync();
    await database.Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseSchemataExceptionHandler();
app.UseAuthorization();
app.MapSchemataQuotaGrpc();
app.MapControllers();
app.Run();

namespace Schemata.Quota.Grpc.Integration.Tests
{
    public partial class Program;

    public sealed class QuotaTransportDbContext(DbContextOptions<QuotaTransportDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataQuotaAccount> Accounts { get; set; } = null!;
        public DbSet<SchemataQuotaSegment> Segments { get; set; } = null!;
        public DbSet<SchemataQuotaReservation> Reservations { get; set; } = null!;
        public DbSet<SchemataQuotaReservationItem> ReservationItems { get; set; } = null!;
        public DbSet<SchemataQuotaLedgerEntry> Ledger { get; set; } = null!;
    }

    internal sealed class StaticQuotaScopeResolver(QuotaScope scope) : IQuotaScopeResolver
    {
        public ValueTask<QuotaScope> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult(scope);
    }
}
