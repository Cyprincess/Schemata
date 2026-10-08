using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Core;
using Schemata.Entity.Repository;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Queries;
using Xunit;

namespace Schemata.Insight.Tests;

/// <summary>
///     A source alias admitted by the public validator must lower to the public row shape before
///     repository filter and order compilation (#179).
/// </summary>
public class InsightSourceAliasShould
{
    public sealed class Person
    {
        public string? Name { get; set; }
        public int     Age  { get; set; }
    }

    [Fact]
    public async Task Alias_Qualified_Aip_Filter_And_Order_Match_Bare_Paths() {
        await using var services = CreateServices();
        var             insight  = services.GetRequiredService<IInsightService>();

        var qualified = await QueryAsync(insight, AipLanguage.Name, "s.age >= 20", "s.age desc");
        var bare      = await QueryAsync(insight, AipLanguage.Name, "age >= 20", "age desc");

        Assert.Equal(["senior", "mature"], qualified.Select(row => row["name"]));
        Assert.Equal(qualified.Select(row => row["age"]), bare.Select(row => row["age"]));
        Assert.Equal(qualified.Select(row => row["name"]), bare.Select(row => row["name"]));
    }

    [Fact]
    public async Task Alias_Qualified_Cel_Filter_And_Order_Match_Bare_Paths() {
        await using var services = CreateServices();
        var             insight  = services.GetRequiredService<IInsightService>();

        var qualified = await QueryAsync(insight, CelLanguage.Name, "s.age >= 20", "s.age desc");
        var bare      = await QueryAsync(insight, CelLanguage.Name, "age >= 20", "age desc");

        Assert.Equal(["senior", "mature"], qualified.Select(row => row["name"]));
        Assert.Equal(qualified.Select(row => row["age"]), bare.Select(row => row["age"]));
        Assert.Equal(qualified.Select(row => row["name"]), bare.Select(row => row["name"]));
    }

    [Fact]
    public async Task Unknown_Alias_Fails_With_The_Existing_Admission_Error() {
        await using var services = CreateServices();
        var             insight  = services.GetRequiredService<IInsightService>();

        await Assert.ThrowsAsync<InsightValidationException>(() => QueryAsync(insight, AipLanguage.Name, "q.age >= 20", null));
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        IInsightService insight,
        string          language,
        string          filter,
        string?         orderBy
    ) {
        var request = new QueryInsightRequest {
            Sources  = [new SourceBinding("s", "people")],
            Language = language,
            Transformations = {
                new TransformationSpec { Filter = new(new InsightExpression(filter)) },
            },
        };
        if (orderBy is not null) {
            request.Transformations.Add(new TransformationSpec { OrderBy = new(orderBy) });
        }

        var response = await insight.QueryAsync(request, null);
        return response.Rows.ToList();
    }

    private static ServiceProvider CreateServices() {
        Person[] rows = [
            new() { Name = "junior", Age = 12 },
            new() { Name = "mature", Age = 34 },
            new() { Name = "senior", Age = 56 },
        ];

        var repository = new Mock<IRepository<Person>>();
        repository.Setup(r => r.ListAsync<Person>(
                              It.IsAny<Func<IQueryable<Person>, IQueryable<Person>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<Person>, IQueryable<Person>> query, CancellationToken ct) =>
                           EntityRows(query(rows.AsQueryable()), ct));

        var services = new ServiceCollection();
        new SchemataInsightBuilder(new SchemataOptions(), services)
            .AddRepositorySource<Person, Person>("people", p => p);
        services.AddSchemataInsight();
        services.AddAipExpressions();
        services.AddCelExpressions();
        services.AddOrderExpressions();
        services.AddSingleton(repository.Object);
        services.AddKeyedSingleton<ISourceDriver>(RepositoryDriver.DriverName,
            static (services, _) => new RepositoryDriver(services));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async IAsyncEnumerable<Person> EntityRows(
        IEnumerable<Person> rows,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var row in rows) {
            ct.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }
}
