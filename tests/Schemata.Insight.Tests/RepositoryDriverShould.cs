using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Order;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class RepositoryDriverShould
{
    [Fact]
    public async Task Applies_Pushable_Filter() {
        var result = await ExecuteAsync("age = 18");

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal(18, row["age"]));
        Assert.Contains(result.Rows, row => (string?)row["full_name"] == "Ada Lovelace");
    }

    [Fact]
    public async Task Applies_Residual_Filter_In_Memory() {
        var result = await ExecuteAsync("profile.locale = 'en'");

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal("en", ((StudentProfile?)row["profile"])?.Locale));
    }

    [Fact]
    public async Task Applies_Order_To_Repository_Query() {
        var result = await ExecuteAsync("age >= 0");

        Assert.Equal([21, 18, 18], result.Rows.Select(row => Assert.IsType<int>(row["age"])));
    }

    [Fact]
    public async Task Materializes_Empty_Selection_With_Snake_Case_Keys() {
        var result = await ExecuteAsync("age = 18");
        var row = Assert.Single(result.Rows, r => (string?)r["name"] == "ada");

        Assert.True(row.ContainsKey("age"));
        Assert.True(row.ContainsKey("full_name"));
    }

    [Fact]
    public async Task Builds_Field_Descriptor_Schema() {
        var result = await ExecuteAsync("age = 18");

        var age = Assert.Single(result.Schema, field => field.Name == "age");
        Assert.Equal(FieldType.Int64, age.Type);
        Assert.Equal("c", age.SourceAlias);

        var fullName = Assert.Single(result.Schema, field => field.Name == "full_name");
        Assert.Equal(FieldType.String, fullName.Type);
    }

    private static async Task<(List<IReadOnlyDictionary<string, object?>> Rows, IReadOnlyList<FieldDescriptor> Schema)> ExecuteAsync(
        string filter
    ) {
        var students = new List<Student> {
            new() {
                Age = 18,
                FullName = "Ada Lovelace",
                Name = "ada",
                CanonicalName = "students/ada",
                Profile = new StudentProfile { Locale = "en" },
            },
            new() {
                Age = 21,
                FullName = "Grace Hopper",
                Name = "grace",
                CanonicalName = "students/grace",
                Profile = new StudentProfile { Locale = "en" },
            },
            new() {
                Age = 18,
                FullName = "Katherine Johnson",
                Name = "katherine",
                CanonicalName = "students/katherine",
                Profile = new StudentProfile { Locale = "fr" },
            },
        };

        var repository = new Mock<IRepository<Student>>();
        repository
           .Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                                   It.IsAny<CancellationToken>()))
           .Returns((Func<IQueryable<Student>, IQueryable<Student>> q, CancellationToken ct)
                        => AsyncList(q(students.AsQueryable()), ct));

        var services = new ServiceCollection();
        services.AddAipExpressions();
        services.AddOrderExpressions();
        services.AddSingleton(repository.Object);
        var sp = services.BuildServiceProvider();

        var compiler = sp.GetRequiredKeyedService<IExpressionCompiler>("aip");
        var parsed = new ParsedExpression(compiler.Parse(filter), "aip", ExpressionKind.Predicate);
        var config = new SourceConfig("repository", new Dictionary<string, object?> { ["resource"] = "students" });
        var source = new SourceNode("c", config);
        var root = new SelectionNode(
            new OrderNode(new FilterNode(source, parsed), "age desc"),
            ImmutableArray<SelectionItem>.Empty);
        var subPlan = new SubPlan(root, "c", config);

        var driver = new RepositoryDriver(sp);
        await using var sourceResult = await driver.ExecuteAsync(subPlan, new QueryInsightRequest(), null, CancellationToken.None);

        return (await sourceResult.Rows.ToListAsync(), sourceResult.Schema);
    }

    private static async IAsyncEnumerable<T> AsyncList<T>(
        IQueryable<T> source,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();
            yield return await Task.FromResult(item);
        }
    }

    [CanonicalName("students/{student}")]
    public sealed class Student : ICanonicalName
    {
        public int Age { get; set; }
        public string? FullName { get; set; }
        public StudentProfile? Profile { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class StudentProfile
    {
        public string? Locale { get; set; }
    }
}

internal static class AsyncEnumerableTestExtensions
{
    public static async Task<List<T>> ToListAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken ct = default
    ) {
        var results = new List<T>();
        await foreach (var item in source.WithCancellation(ct)) {
            results.Add(item);
        }

        return results;
    }
}
