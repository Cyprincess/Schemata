using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Expressions.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceOperationHandler;

public class OperationHandlerListShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task List_ReturnsAllStudents_WhenNoFilter() {
        var handler = _fixture.CreateHandler();

        var result = await handler.ListAsync(new(), null, null);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalSize);
        Assert.Equal(2, result.Entities?.Count());
    }

    [Fact]
    public async Task List_WithFilter_AppliesPredicateToRepository() {
        var handler = _fixture.CreateHandler();
        var request = new ListRequest { Filter = "age = 18" };

        var result = await handler.ListAsync(request, null, null);

        Assert.Equal(1, result.TotalSize);
        Assert.Equal(1, result.Entities?.Count());
    }

    [Fact]
    public async Task List_WithInvalidFilter_ThrowsValidationException() {
        var handler = _fixture.CreateHandler();
        var request = new ListRequest { Filter = "(" };

        await Assert.ThrowsAsync<ValidationException>(() => handler.ListAsync(request, null, null));
    }

    [Fact]
    public async Task List_WithMalformedParent_ThrowsValidationException() {
        var handler = _fixture.CreateHandler();
        var request = new ListRequest { Parent = "garbage/value" };

        await Assert.ThrowsAsync<ValidationException>(() => handler.ListAsync(request, null, null));
    }

    [Fact]
    public async Task List_WithEmptyParent_ListsTopLevel() {
        var handler = _fixture.CreateHandler();
        var request = new ListRequest { Parent = "" };

        var result = await handler.ListAsync(request, null, null);

        Assert.Equal(2, result.TotalSize);
    }

    [Fact]
    public async Task List_WithOrderBy_AppliesOrderingToRepository() {
        var handler = _fixture.CreateHandler();
        var request = new ListRequest { OrderBy = "age desc" };

        var result = await handler.ListAsync(request, null, null);

        var ages = result.Entities!.Select(s => ((Student)s).Age).ToArray();

        Assert.Equal([19, 18], ages);
    }

    [Fact]
    public async Task List_WithInvalidOrderBy_ThrowsValidationException() {
        var handler = _fixture.CreateHandler();
        var request = new ListRequest { OrderBy = "(" };

        await Assert.ThrowsAsync<ValidationException>(() => handler.ListAsync(request, null, null));
    }

    [Fact]
    public async Task List_WithOrderBy_UsesRegisteredOrderCompiler() {
        var handler = _fixture.CreateHandler(services => {
            services.AddSingleton<IOrderCompiler>(new AscendingAgeOrderCompiler());
        });
        var request = new ListRequest { OrderBy = "age desc" };

        var result = await handler.ListAsync(request, null, null);

        var ages = result.Entities!.Select(s => ((Student)s).Age).ToArray();

        Assert.Equal([18, 19], ages);
    }

    [Fact]
    public async Task List_WithoutOrderBy_OrdersByKey() {
        _fixture.Students.Insert(0, new() {
            Uid           = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            FullName      = "Zoe",
            Age           = 20,
            Grade         = 3,
            Name          = "zoe-9",
            CanonicalName = "students/zoe-9",
        });

        var handler = _fixture.CreateHandler();
        var result  = await handler.ListAsync(new(), null, null);

        var uids = result.Entities!.Select(s => ((Student)s).Uid).ToArray();

        Assert.Equal(uids.OrderBy(u => u).ToArray(), uids);
    }

    [Fact]
    public async Task List_WithOrderBy_BreaksTiesByKey() {
        _fixture.Students.Clear();
        _fixture.Students.Add(new() {
            Uid           = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            FullName      = "Dave",
            Age           = 18,
            Name          = "dave-4",
            CanonicalName = "students/dave-4",
        });
        _fixture.Students.Add(new() {
            Uid           = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            FullName      = "Carol",
            Age           = 18,
            Name          = "carol-3",
            CanonicalName = "students/carol-3",
        });

        var handler = _fixture.CreateHandler();
        var result  = await handler.ListAsync(new() { OrderBy = "age" }, null, null);

        var names = result.Entities!.Select(s => ((Student)s).Name).ToArray();

        Assert.Equal(new[] { "carol-3", "dave-4" }, names);
    }

    [Fact]
    public async Task List_WithPageSize_LimitsResults() {
        for (var i = 3; i <= 5; i++) {
            _fixture.Students.Add(new() {
                                      Uid      = Identifiers.NewUid(),
                                      FullName = $"Student{i}",
                                      Age      = 20 + i,
                                      Grade    = i,
                                      Name     = $"students/student-{i}",
                                  });
        }

        var handler = _fixture.CreateHandler();
        var result  = await handler.ListAsync(new() { PageSize = 2 }, null, null);

        Assert.NotNull(result.NextPageToken);
    }

    [Fact]
    public async Task List_LastPageExactlyFull_OmitsNextPageToken() {
        for (var i = 3; i <= 4; i++) {
            _fixture.Students.Add(new() {
                Uid = Guid.Parse($"{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }-{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }-{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }-{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }-{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }{
                    i
                }"),
                FullName      = $"Student{i}",
                Age           = 20 + i,
                Name          = $"student-{i}",
                CanonicalName = $"students/student-{i}",
            });
        }

        var handler = _fixture.CreateHandler();

        var page1 = await handler.ListAsync(new() { PageSize = 2 }, null, null);
        Assert.Equal(2, page1.Entities?.Count());
        Assert.NotNull(page1.NextPageToken);

        var page2 = await handler.ListAsync(new() { PageSize = 2, PageToken = page1.NextPageToken }, null, null);
        Assert.Equal(2, page2.Entities?.Count());
        Assert.Null(page2.NextPageToken);
    }

    [Fact]
    public async Task List_NegativePageSize_ThrowsValidationException() {
        var handler = _fixture.CreateHandler();

        await Assert.ThrowsAsync<ValidationException>(() => handler.ListAsync(new() { PageSize = -1 }, null, null));
    }

    [Fact]
    public async Task List_ZeroPageSize_UsesDefault() {
        var handler = _fixture.CreateHandler();

        var result = await handler.ListAsync(new() { PageSize = 0 }, null, null);

        Assert.Equal(2, result.Entities?.Count());
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task List_TotalSizeNone_OmitsTotalAndSkipsCount() {
        var handler = _fixture.CreateHandler(services => {
            services.AddSingleton(Options.Create(new SchemataResourceOptions { TotalSize = TotalSizeMode.None }));
        });

        var result = await handler.ListAsync(new(), null, null);

        Assert.Null(result.TotalSize);
        Assert.Equal(2, result.Entities?.Count());
        _fixture.Repository.Verify(
            r => r.CountAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                              It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_TotalSizeEstimated_UsesEstimate() {
        _fixture.Repository
                .Setup(r => r.EstimateCountAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                                                 It.IsAny<CancellationToken>()))
                .ReturnsAsync(42L);

        var handler = _fixture.CreateHandler(services => {
            services.AddSingleton(Options.Create(new SchemataResourceOptions { TotalSize = TotalSizeMode.Estimated }));
        });

        var result = await handler.ListAsync(new(), null, null);

        Assert.Equal(42, result.TotalSize);
    }

    [Fact]
    public async Task List_TotalSizePerResourceOverride_WinsOverGlobal() {
        var options = new SchemataResourceOptions { TotalSize = TotalSizeMode.Exact };
        options.Resources[typeof(Student).TypeHandle] = new(typeof(Student)) { TotalSize = TotalSizeMode.None };

        var handler = _fixture.CreateHandler(services => { services.AddSingleton(Options.Create(options)); });

        var result = await handler.ListAsync(new(), null, null);

        Assert.Null(result.TotalSize);
    }

    [Fact]
    public async Task List_ShowDeleted_CallsSuppressQuerySoftDelete() {
        var handler = _fixture.CreateHandler();

        await handler.ListAsync(new() { ShowDeleted = true }, null, null);

        _fixture.Repository.Verify(r => r.SuppressQuerySoftDelete(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task List_ResidualFilter_AppliesResidualLocally() {
        SeedLocales();
        var handler = CreateResidualHandler();

        var result = await handler.ListAsync(new() { Filter = "profile.locale = 'en'" }, null, null);

        Assert.Equal(2, result.Entities?.Count());
    }

    [Fact]
    public async Task List_ResidualFilter_ExactTotal_CountsMatches() {
        SeedLocales();
        var handler = CreateResidualHandler(TotalSizeMode.Exact);

        var result = await handler.ListAsync(new() { Filter = "profile.locale = 'en'" }, null, null);

        Assert.Equal(2, result.TotalSize);
    }

    [Fact]
    public async Task List_ResidualFilter_NoneTotal_OmitsTotal() {
        SeedLocales();
        var handler = CreateResidualHandler(TotalSizeMode.None);

        var result = await handler.ListAsync(new() { Filter = "profile.locale = 'en'" }, null, null);

        Assert.Null(result.TotalSize);
        Assert.Equal(2, result.Entities?.Count());
    }

    [Fact]
    public async Task List_ResidualFilter_PaginatesAfterResidual() {
        SeedLocales();
        var handler = CreateResidualHandler(TotalSizeMode.None);

        var page1 = await handler.ListAsync(new() { Filter = "profile.locale = 'en'", PageSize = 1 }, null, null);
        Assert.Single(page1.Entities!);
        Assert.NotNull(page1.NextPageToken);

        var page2 = await handler.ListAsync(
            new() { Filter = "profile.locale = 'en'", PageSize = 1, PageToken = page1.NextPageToken }, null, null);
        Assert.Single(page2.Entities!);
        Assert.Null(page2.NextPageToken);
    }

    [Fact]
    public async Task List_ResidualFilter_ExceedingScanCap_Throws() {
        SeedLocales();
        var handler = CreateResidualHandler(cap: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ListAsync(new() { Filter = "profile.locale = 'en'" }, null, null));
    }

    [Fact]
    public async Task List_ResidualFilter_PushesFlatConjunct_ShrinkingScanBelowCap() {
        _fixture.Students.Clear();
        // Without pushing `age = 18`, the residual would scan all 6 students and exceed the cap of 4.
        // Partial pushdown sends `age = 18` to the backend, so only the 3 age-18 rows are scanned.
        for (var i = 0; i < 3; i++) {
            _fixture.Students.Add(NewAgedStudent($"x{i}", 20, "en"));
        }

        _fixture.Students.Add(NewAgedStudent("a", 18, "en"));
        _fixture.Students.Add(NewAgedStudent("b", 18, "en"));
        _fixture.Students.Add(NewAgedStudent("c", 18, "fr"));

        var handler = CreateResidualHandler(cap: 4);

        var result = await handler.ListAsync(
            new() { Filter = "age = 18 AND profile.locale = 'en'" }, null, null);

        Assert.Equal(2, result.Entities?.Count());
    }

    private void SeedLocales() {
        _fixture.Students.Clear();
        _fixture.Students.Add(NewStudent("a", "en"));
        _fixture.Students.Add(NewStudent("b", "fr"));
        _fixture.Students.Add(NewStudent("c", "en"));
    }

    private static Student NewStudent(string name, string locale) {
        return new() {
            Uid           = Identifiers.NewUid(),
            Name          = name,
            CanonicalName = $"students/{name}",
            Profile       = new() { Locale = locale },
        };
    }

    private static Student NewAgedStudent(string name, int age, string locale) {
        return new() {
            Uid           = Identifiers.NewUid(),
            Name          = name,
            CanonicalName = $"students/{name}",
            Age           = age,
            Profile       = new() { Locale = locale },
        };
    }

    private ResourceOperationHandler<Student, Student, Student, Student> CreateResidualHandler(
        TotalSizeMode? totalSize = null,
        int            cap       = 0
    ) {
        return _fixture.CreateHandler(services => {
            var options = new SchemataResourceOptions();
            if (totalSize is { } mode) {
                options.TotalSize = mode;
            }

            var entry = options.Expressions.Enable(ExpressionLanguages.Aip);
            entry.Filtering = FilteringMode.Residual;
            if (cap > 0) {
                entry.MaxResidualScanRows = cap;
            }

            services.AddSingleton(Options.Create(options));
        });
    }

    #region Nested type: AscendingAgeOrderCompiler

    private sealed class AscendingAgeOrderCompiler : IOrderCompiler
    {
        #region IOrderCompiler Members

        public Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
            string                    source,
            ExpressionCompileOptions? options = null
        ) {
            var parameter = Expression.Parameter(typeof(T), "entity");
            var body      = Expression.PropertyOrField(parameter, nameof(Student.Age));
            var key       = Expression.Lambda<Func<T, int>>(body, parameter);
            return query => query.OrderBy(key);
        }

        public IReadOnlyList<OrderKey> Parse(string source) {
            return [new(["age"], false)];
        }

        #endregion
    }

    #endregion
}
