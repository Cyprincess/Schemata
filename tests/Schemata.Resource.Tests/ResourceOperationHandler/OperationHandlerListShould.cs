using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Skeleton;
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

        // Only Alice (age 18) should match
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
            services.AddKeyedSingleton<IOrderCompiler>(AipLanguage.Name, new AscendingAgeOrderCompiler());
        });
        var request = new ListRequest { OrderBy = "age desc" };

        var result = await handler.ListAsync(request, null, null);

        var ages = result.Entities!.Select(s => ((Student)s).Age).ToArray();

        Assert.Equal([18, 19], ages);
    }

    [Fact]
    public async Task List_WithPageSize_LimitsResults() {
        for (var i = 3; i <= 5; i++) {
            _fixture.Students.Add(new() {
                                      Uid      = Guid.NewGuid(),
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
    public async Task List_ShowDeleted_CallsSuppressQuerySoftDelete() {
        var handler = _fixture.CreateHandler();

        await handler.ListAsync(new() { ShowDeleted = true }, null, null);

        _fixture.Repository.Verify(r => r.SuppressQuerySoftDelete(), Times.AtLeastOnce);
    }

    #region Nested type: AscendingAgeOrderCompiler

    private sealed class AscendingAgeOrderCompiler : IOrderCompiler
    {
        #region IOrderCompiler Members

        public string Language => AipLanguage.Name;

        public Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
            string                    source,
            ExpressionCompileOptions? options = null
        ) {
            var parameter = Expression.Parameter(typeof(T), "entity");
            var body      = Expression.PropertyOrField(parameter, nameof(Student.Age));
            var key       = Expression.Lambda<Func<T, int>>(body, parameter);
            return query => query.OrderBy(key);
        }

        #endregion
    }

    #endregion
}
