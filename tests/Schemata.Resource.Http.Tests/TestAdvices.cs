using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Schemata.Resource.Http.Tests;

public class TestAdvices : IDisposable
{
    private readonly List<Student> _students;

    private readonly Student _student;

    private readonly TestFixture _fixture;

    public TestAdvices() {
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
    public async Task ListEntitiesProperty() {
        var result = await _fixture.Controller.List(new());

        var json   = Assert.IsType<JsonResult>(result);
        var action = new ActionContext() { HttpContext = _fixture.Context };

        await json.ExecuteResultAsync(action);

        _fixture.Body.Seek(0, SeekOrigin.Begin);

        var raw = Encoding.UTF8.GetString(_fixture.Body.ToArray());

        Assert.Contains("\"students\":", raw);
    }

    [Fact]
    public async Task GetFreshnessTag() {
        var result = await _fixture.Controller.Get(_student.Id);

        var json   = Assert.IsType<JsonResult>(result);
        var action = new ActionContext() { HttpContext = _fixture.Context };

        await json.ExecuteResultAsync(action);

        _fixture.Body.Seek(0, SeekOrigin.Begin);

        var raw = Encoding.UTF8.GetString(_fixture.Body.ToArray());

        Assert.Contains("\"etag\":", raw);
    }

    public void Dispose() {
        _fixture.Dispose();
    }
}
