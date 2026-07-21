using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests.Extensions;

public class QueryableExtensionsShould
{
    [Fact]
    public void ApplyFilter_ValidAipFilter_NarrowsQuery() {
        var compiler = new Mock<IExpressionCompiler>();
        compiler.Setup(c => c.Parse("age >= 18")).Returns(Mock.Of<IExpressionTree>());
        compiler.Setup(c => c.Compile<FilterEntity, bool>(It.IsAny<IExpressionTree>(), It.IsAny<ExpressionCompileOptions?>()))
                .Returns((FilterEntity entity) => entity.Age >= 18);
        using var services = new ServiceCollection().AddKeyedSingleton<IExpressionCompiler>(ExpressionLanguages.Aip, compiler.Object).BuildServiceProvider();
        var request = new FilterRequest { Filter = "age >= 18" };
        var entities = new[] {
            new FilterEntity { Age = 9 }, new FilterEntity { Age = 18 }, new FilterEntity { Age = 25 },
        }.AsQueryable();

        var ages = entities.ApplyFilter(request, services).Select(entity => entity.Age).ToArray();

        Assert.Equal([18, 25], ages);
    }

    [Fact]
    public void ApplyFilter_EmptyFilter_ReturnsOriginalQuery() {
        using var services = new ServiceCollection().BuildServiceProvider();
        var request  = new FilterRequest();
        var entities = new[] { new FilterEntity { Age = 18 } }.AsQueryable();

        var filtered = entities.ApplyFilter(request, services);

        Assert.Same(entities, filtered);
    }

    [Fact]
    public void ApplyFilter_MalformedFilter_ThrowsInvalidArgumentWithFilterViolation() {
        var compiler = new Mock<IExpressionCompiler>();
        compiler.Setup(c => c.Parse("?!")).Throws<ArgumentException>();
        using var services = new ServiceCollection().AddKeyedSingleton<IExpressionCompiler>(ExpressionLanguages.Aip, compiler.Object).BuildServiceProvider();
        var request  = new FilterRequest { Filter = "?!" };
        var entities = new[] { new FilterEntity { Age = 18 } }.AsQueryable();

        var exception = Assert.Throws<InvalidArgumentException>(() => entities.ApplyFilter(request, services));

        var violation = Assert.Single(Assert.Single(exception.Details!.OfType<BadRequestDetail>()).FieldViolations!);
        Assert.Equal("filter", violation.Field);
        Assert.Equal("INVALID_FILTER", violation.Reason);
    }

    private sealed class FilterEntity
    {
        public int Age { get; init; }
    }

    private sealed class FilterRequest : IFilterRequest
    {
        public string? Filter { get; init; }
    }
}
