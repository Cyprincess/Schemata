using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Core;
using Schemata.Entity.Repository;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Catalog;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;
using Xunit;

namespace Schemata.Insight.Tests;

/// <summary>
///     The module's enabled-language profile, resolved through ExpressionLanguageResolver, governs
///     Insight admission and execution: globally registered but disabled languages are rejected, the
///     first enabled language is the default, and per-language filtering and scan caps reach
///     repository pushdown and residual execution.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExpressionLanguagePolicyShould
{
    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Rejects_A_Globally_Registered_Language_The_Profile_Disables() {
        await using var services = PlanningServices(Compiler("age > 1"), Compiler("age > 1"));
        var planner = new InsightPlanBuilder([Catalog()], services,
            Options.Create(new SchemataInsightOptions { Expressions = Profile("aip") }));

        var error = await Assert.ThrowsAsync<InsightValidationException>(async () => {
            await planner.BuildAsync(FilterRequest("cel"), CancellationToken.None);
        });

        Assert.Equal(InsightReasons.UnknownExpressionLanguage, error.Reason);
        Assert.Equal("cel", error.Metadata!["language"]);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Defaults_Expressions_To_The_First_Enabled_Profile_Language() {
        var cel = Compiler("age > 1");
        var aip = new Mock<IExpressionCompiler>(MockBehavior.Strict);
        await using var services = PlanningServices(cel, aip);
        var planner = new InsightPlanBuilder([Catalog()], services,
            Options.Create(new SchemataInsightOptions { Expressions = Profile("cel", "aip") }));

        var plan = await planner.BuildAsync(FilterRequest(null), CancellationToken.None);

        var filter = Assert.IsType<FilterNode>(Assert.IsType<SelectionNode>(Assert.IsType<LimitNode>(plan).Input).Input);
        Assert.Equal("cel", filter.Predicate.Language);
        cel.Verify(compiler => compiler.Parse("age > 1"), Times.Once);
        aip.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Bounds_Repository_Residual_Supersets_By_The_Per_Language_Scan_Cap() {
        await using var services = RepositoryServices(builder => builder.UseCel(cel => {
            cel.Filtering          = FilteringMode.Residual;
            cel.MaxResidualScanRows = 3;
        }));

        var error = await Assert.ThrowsAsync<InsightValidationException>(
            () => QueryAsync(services, "name.matches('.*')"));

        Assert.Equal(InsightReasons.InvalidArgument, error.Reason);
        Assert.Equal("3", error.Metadata!["cap"]);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Pushes_The_Whole_Filter_And_Leaves_The_Backend_Read_Uncapped_When_Not_Residual() {
        await using var services = RepositoryServices(builder => {
            builder.UseCel();
            builder.Languages.MaxResidualScanRows = 2;
        });

        var rows = await QueryAsync(services, "age >= 20");

        Assert.Equal(["mature", "senior", "elder"], rows.Select(row => row["name"]));
    }

    private static QueryInsightRequest FilterRequest(string? language) {
        return new() {
            Sources = [new SourceBinding("s", "people")],
            Language = language,
            Transformations = { new TransformationSpec { Filter = new(new InsightExpression("age > 1")) } },
        };
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        ServiceProvider services,
        string filter
    ) {
        var insight = services.GetRequiredService<IInsightService>();
        var response = await insight.QueryAsync(new() {
            Sources = [new SourceBinding("s", "people")],
            Transformations = { new TransformationSpec { Filter = new(new InsightExpression(filter)) } },
        }, null);
        return response.Rows.ToList();
    }

    private static IInsightSourceCatalog Catalog() {
        return new InMemoryInsightSourceCatalog(new Dictionary<string, SourceConfig> {
            ["people"] = new("probe", new Dictionary<string, object?>()),
        });
    }

    private static ExpressionLanguageProfile Profile(params string[] languages) {
        var profile = new ExpressionLanguageProfile();
        foreach (var language in languages) {
            profile.Enable(language);
        }

        return profile;
    }

    private static Mock<IExpressionCompiler> Compiler(string source) {
        var compiler = new Mock<IExpressionCompiler>(MockBehavior.Strict);
        compiler.Setup(value => value.Parse(source)).Returns(new Mock<IExpressionTree>().Object);
        return compiler;
    }

    private static ServiceProvider PlanningServices(Mock<IExpressionCompiler> cel, Mock<IExpressionCompiler> aip) {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IExpressionCompiler>("cel", cel.Object);
        services.AddKeyedSingleton<IExpressionCompiler>("aip", aip.Object);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider RepositoryServices(Action<SchemataInsightBuilder> configure) {
        Person[] people = [
            new() { Name = "junior", Age = 12 },
            new() { Name = "mature", Age = 34 },
            new() { Name = "senior", Age = 56 },
            new() { Name = "elder", Age = 78 },
        ];

        var repository = new Mock<IRepository<Person>>();
        repository.Setup(r => r.ListAsync<Person>(
                              It.IsAny<Func<IQueryable<Person>, IQueryable<Person>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<Person>, IQueryable<Person>> query, CancellationToken ct, QueryOptions _) =>
                           Rows(query(people.AsQueryable()), ct));

        var services = new ServiceCollection();
        var builder = new SchemataInsightBuilder(new SchemataOptions(), services);
        configure(builder);
        builder.AddRepositorySource<Person, Person>("people", p => p);
        services.AddSchemataInsight();
        services.AddCelExpressions();
        services.AddSingleton(repository.Object);
        services.AddKeyedSingleton<ISourceDriver>(RepositoryDriver.DriverName,
            static (services, _) => new RepositoryDriver(services));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async IAsyncEnumerable<Person> Rows(
        IEnumerable<Person>                    people,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var person in people) {
            ct.ThrowIfCancellationRequested();
            yield return person;
            await Task.Yield();
        }
    }

    public sealed class Person
    {
        public string? Name { get; set; }
        public int     Age  { get; set; }
    }
}
