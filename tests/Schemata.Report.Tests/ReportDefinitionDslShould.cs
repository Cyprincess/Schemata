using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Common;
using Schemata.Core;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;
using Xunit;

namespace Schemata.Report.Tests;

public class ReportDefinitionDslShould
{
    [Fact]
    public async Task Define_Produces_Equivalent_QueryInsightRequest() {
        var services = new ServiceCollection();
        var report = CreateBuilder(services);
        report.Define("daily-sales", definition => definition
            .From("orders", alias: "o")
            .Where("status == 'PAID'", language: ExpressionLanguages.Cel)
            .GroupBy(keys: ["region"], aggregations => aggregations.Sum("amount", into: "total"))
            .Select("region")
            .SelectExpression("total", "total", language: ExpressionLanguages.Cel));

        using var provider = services.BuildServiceProvider();
        var definition = provider.GetRequiredKeyedService<IReportDefinitionProvider>("daily-sales");
        var actual = await definition.GetDefinitionAsync();
        var expected = new QueryInsightRequest {
            Sources = [new("o", "orders")],
            Transformations = [
                new() { Filter = new(new("status == 'PAID'", ExpressionLanguages.Cel)) },
                new() {
                    GroupBy = new(
                        ImmutableArray.Create("region"),
                        ImmutableArray.Create(new AggregationSpec("amount", AggregationFunction.Sum, "total"))
                    ),
                },
            ],
            Selections = [
                new() { Field = "region" },
                new() { Expression = new("total", ExpressionLanguages.Cel), Alias = "total" },
            ],
        };

        Assert.Null(actual.Language);
        Assert.Equal(Serialize(expected), Serialize(actual));
    }

    [Fact]
    public async Task Define_Registers_Keyed_Provider() {
        var services = new ServiceCollection();
        var report = CreateBuilder(services);
        report.Define("sales", definition => definition.From("orders", alias: "o"));

        using var provider = services.BuildServiceProvider();
        var definition = provider.GetRequiredKeyedService<IReportDefinitionProvider>("sales");
        var first = await definition.GetDefinitionAsync();
        var second = await definition.GetDefinitionAsync();

        Assert.NotSame(first, second);
        Assert.Equal(Serialize(first), Serialize(second));
    }

    [Fact]
    public async Task Periodic_Sets_Schedule_Metadata() {
        var services = new ServiceCollection();
        var report = CreateBuilder(services);
        report.Define("daily-sales", definition => definition
            .From("orders", alias: "o")
            .Periodic(cron: "0 0 * * *")
            .Retain(days: 30));
        report.Define("hourly-sales", definition => definition
            .From("orders", alias: "o")
            .Periodic(interval: TimeSpan.FromHours(1))
            .Retain(count: 24));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SchemataReportOptions>>().Value;
        var daily = Assert.Single(options.Definitions, item => item.Name == "daily-sales");
        var hourly = Assert.Single(options.Definitions, item => item.Name == "hourly-sales");

        Assert.True(daily.Periodic);
        Assert.Equal(ReportScheduleKind.Cron, daily.ScheduleKind);
        Assert.Equal("0 0 * * *", daily.CronExpression);
        Assert.Null(daily.IntervalTicks);
        Assert.Equal(30, daily.Retention!.MaxAgeDays);
        Assert.True(hourly.Periodic);
        Assert.Equal(ReportScheduleKind.Periodic, hourly.ScheduleKind);
        Assert.Null(hourly.CronExpression);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, hourly.IntervalTicks);
        Assert.Equal(24, hourly.Retention!.MaxCount);

        var store = new ConfigurationReportDefinitionStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<SchemataReportOptions>>()
        );
        var periodic = new List<SchemataReport>();
        await foreach (var definition in store.ListPeriodicAsync(default)) {
            periodic.Add(definition);
        }

        Assert.Collection(
            periodic,
            definition => Assert.Equal("daily-sales", definition.Name),
            definition => Assert.Equal("hourly-sales", definition.Name)
        );
    }

    [Fact]
    public void Duplicate_Define_Name_Throws() {
        var services = new ServiceCollection();
        var report = CreateBuilder(services);
        report.Define("sales", definition => definition.From("orders", alias: "o"));

        var duplicate = Assert.Throws<ArgumentException>(() => {
            report.Define("sales", definition => definition.From("orders", alias: "o"));
        });
        var empty = Assert.Throws<ArgumentException>(() => {
            report.Define(" ", definition => definition.From("orders", alias: "o"));
        });

        Assert.Contains("sales", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("name", empty.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk> CreateBuilder(
        IServiceCollection services
    ) {
        var builder = new SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(
            new SchemataOptions(),
            services
        );
        return builder;
    }

    private static string Serialize(QueryInsightRequest request) {
        return JsonSerializer.Serialize(request, SchemataJson.Default);
    }
}
