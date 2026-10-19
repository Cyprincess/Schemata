using Schemata.Report.Tests.Fixtures;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Definitions;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Skeleton;
using Xunit;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Tests;

public class ReportRepositoryDependencyShould
{
    [Fact]
    public async Task Run_Inline_Without_Repositories_Succeeds() {
        var       driver   = ReportTestHost.CreateDriver(ReportTestRows.Create(2));
        using var provider = ReportTestHost.Create(driver, registerRepositories: false);
        var       service  = provider.GetRequiredService<IReportService>();

        var result = await service.RunAsync(ReportTestHost.InlineRequest());

        Assert.Equal(2, result.Response.Rows.Count);
    }

    [Fact]
    public async Task Run_Configuration_Named_Without_Repositories_Succeeds() {
        var driver = ReportTestHost.CreateDriver(ReportTestRows.Create(1));
        using var provider = ReportTestHost.Create(
            driver,
            registerRepositories: false,
            configure: services => {
                services.Configure<SchemataReportOptions>(options => options.Definitions.Add(new() {
                    Name  = "daily",
                    Query = new() { Sources = [new("r", "rows")] },
                }));
                services.AddSingleton<IReportDefinitionSource, ConfigurationReportDefinitionStore>();
                services.AddSingleton<IReportDefinitionStore, CompositeReportDefinitionStore>();
            });
        var service = provider.GetRequiredService<IReportService>();

        var result = await service.RunAsync(new() { Name = "daily" });

        Assert.Single(result.Response.Rows);
    }

    [Fact]
    public async Task Resolve_Materializes_Fresh_Query_Per_Dispatch() {
        var driver = ReportTestHost.CreateDriver(ReportTestRows.Create(1));
        var template = new QueryInsightRequest {
            Sources     = [new("r", "rows")],
            Selections  = [new() { Field = "r.value" }],
        };
        using var provider = ReportTestHost.Create(
            driver,
            registerRepositories: false,
            configure: services => {
                services.Configure<SchemataReportOptions>(options => options.Definitions.Add(new() {
                    Name  = "daily",
                    Query = template,
                }));
                services.AddSingleton<IReportDefinitionSource, ConfigurationReportDefinitionStore>();
                services.AddSingleton<IReportDefinitionStore, CompositeReportDefinitionStore>();
            });
        var store = provider.GetRequiredService<IReportDefinitionStore>();

        var first  = (await store.ResolveAsync("daily"))!.Value.Query;
        var second = (await store.ResolveAsync("daily"))!.Value.Query;

        Assert.NotSame(template, first);
        Assert.NotSame(first, second);
        first.Sources.Add(new("mutated", "rows"));
        first.Selections.Clear();
        Assert.Single(template.Sources);
        Assert.Single(template.Selections);
        Assert.Single(second.Sources);
        Assert.Single(second.Selections);
    }

    [Fact]
    public async Task Run_Database_Named_Without_Repositories_Throws_On_Repository_Resolution() {
        var driver = ReportTestHost.CreateDriver(ReportTestRows.Create(1));
        using var provider = ReportTestHost.Create(
            driver,
            registerRepositories: false,
            configure: services => {
                services.AddSingleton<IReportDefinitionSource, DatabaseReportDefinitionStore<SchemataReport>>();
                services.AddSingleton<IReportDefinitionStore, CompositeReportDefinitionStore>();
            });
        var service = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.RunAsync(new() { Name = "missing" }));

        Assert.Contains("IRepository", exception.Message);
    }

    [Fact]
    public async Task Run_Persisted_Without_Repositories_Throws_On_Repository_Resolution() {
        var       driver   = ReportTestHost.CreateDriver(ReportTestRows.Create(1));
        using var provider = ReportTestHost.Create(driver, registerRepositories: false);
        var       service  = provider.GetRequiredService<IReportService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.RunAsync(ReportTestHost.InlineRequest(persist: true)));

        Assert.Contains("IRepository", exception.Message);
    }
}
