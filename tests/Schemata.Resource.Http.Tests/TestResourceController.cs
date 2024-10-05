using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Schemata.Abstractions.Resource;
using Xunit;

namespace Schemata.Resource.Http.Tests;

public class TestResourceController : IDisposable
{
    private readonly List<Student> _students;

    private readonly Student _student;

    private readonly TestFixture _fixture;

    public TestResourceController() {
        _fixture = new();

       _student = new() {
            Id        = 1,
            Name      = "Alice",
            Age       = 18,
            Grade     = 1,
            Timestamp = Guid.NewGuid(),
        };

        _students = [
            new() {
                Id        = 1,
                Name      = "Alice",
                Age       = 18,
                Grade     = 1,
                Timestamp = Guid.Empty,
            },
            new() {
                Id        = 2,
                Name      = "Bob",
                Age       = 19,
                Grade     = 2,
                Timestamp = Guid.Empty,
            },
        ];

        _fixture.Repository.Setup(r => r.LongCountAsync(
            It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
            It.IsAny<CancellationToken>()
        )).ReturnsAsync(() => _students.Count);
        _fixture.Repository.Setup(r => r.ListAsync(
            It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
            It.IsAny<CancellationToken>()
        )).Returns(ListStudents);

        _fixture.Repository.Setup(r => r.SingleOrDefaultAsync(
            It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
            It.IsAny<CancellationToken>()
        )).ReturnsAsync(() => _student);

        return;

        async IAsyncEnumerable<Student> ListStudents() {
            foreach (var student in _students) {
                yield return await Task.FromResult(student);
            }
        }
    }

    [Fact]
    public async Task List() {
        var result = await _fixture.Controller.List(new());

        var json  = Assert.IsType<JsonResult>(result);
        var response = Assert.IsAssignableFrom<ListResponse<Student>>(json.Value);
        Assert.Equal(response.TotalSize, response.Entities?.Count());
        Assert.Equal(_students.Count, response.TotalSize);
        Assert.Null(response.NextPageToken);
    }

    [Fact]
    public async Task Get() {
        var result = await _fixture.Controller.Get(_student.Id);

        var json     = Assert.IsType<JsonResult>(result);
        var response = Assert.IsAssignableFrom<Student>(json.Value);
        Assert.Equal(_student.Timestamp, response.Timestamp);
        Assert.False(string.IsNullOrWhiteSpace(response.EntityTag));
    }

    public void Dispose() {
        _fixture.Dispose();
    }
}
