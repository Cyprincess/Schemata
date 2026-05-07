using System;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.ResourceOperationHandler;

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
    public async Task List_WithPageSize_LimitsResults() {
        for (var i = 3; i <= 5; i++) {
            _fixture.Students.Add(
                new() {
                    Uid      = Guid.NewGuid(),
                    FullName = $"Student{i}",
                    Age      = 20 + i,
                    Grade    = i,
                    Name     = $"students/student-{i}",
                }
            );
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
}
