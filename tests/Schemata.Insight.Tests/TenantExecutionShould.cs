using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Insight.Skeleton;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Insight.Tests;

[Trait("Layer", "Integration")]
public sealed class TenantExecutionShould
{
    [Fact]
    public async Task Tenant_Scope_Runs_The_Insight_Execution_With_Tenant_Bound_Protection() {
        var tenants = new ConcurrentDictionary<Guid, SchemataTenant>();
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                tenants.GetOrAdd(identifier, static value => new SchemataTenant { Uid = value, Timestamp = Guid.NewGuid(), Name = value.ToString() })));
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(manager.Object)
            .AddSchemataInsight();
        services.Configure<SchemataInsightOptions>(options => options.Expressions.MaxResidualScanRows = 20);
        var builder = new SchemataTenancyBuilder<SchemataTenant>(services);
        builder.UseState(state => state.CookiePrefix = "n.");
        builder.ForTenant((_, tenant, _) => tenant.AddInsightExecution());
        using var root = services.BuildServiceProvider();

        var hostPlans = root.GetRequiredService<InsightPlanBuilder>();
        var hostService = root.GetRequiredService<IInsightService>();
        var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        using var cache = new MemoryCacheTenantProviderCache(tenancy);
        var scopes = new SchemataTenantServiceScopeFactory<SchemataTenant>(
            root, new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, tenancy));
        await using var scope = await scopes.CreateAsync(new(Guid.NewGuid()));

        var provider = scope.ServiceProvider;
        Assert.NotSame(hostPlans, provider.GetRequiredService<InsightPlanBuilder>());
        Assert.NotSame(hostService, provider.GetRequiredService<IInsightService>());
        Assert.NotNull(provider.GetRequiredService<PlanExecutor>());
        Assert.NotNull(provider.GetRequiredService<IRequestHandler<QueryInsightRequest, QueryInsightResponse>>());

        // The execution closure seals continuation material with the profile's fixed protector,
        // so a payload minted inside the tenant scope never opens under the host key material.
        var tenantProtection = provider.GetRequiredService<IDataProtectionProvider>();
        Assert.NotSame(root.GetRequiredService<IDataProtectionProvider>(), tenantProtection);
        var payload = tenantProtection.CreateProtector("tenant-execution").Protect("continuation");
        Assert.Throws<CryptographicException>(
            () => root.GetRequiredService<IDataProtectionProvider>().CreateProtector("tenant-execution").Unprotect(payload));
    }
}
