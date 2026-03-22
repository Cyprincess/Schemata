using System.Linq;
using System.Threading.Tasks;
using Schemata.Resource.Foundation.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Integration.Tests;

public class ResourceFilterIntegrationShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    private async Task SeedAsync() {
        var (handler, scope) = _fixture.CreateHandlerWithScope();
        using (scope) {
            await handler.CreateAsync(new() { FullName = "Alice", Age = 18, Grade = 1 }, null, null);
            await handler.CreateAsync(new() { FullName = "Bob", Age   = 25, Grade = 2 }, null, null);
            await handler.CreateAsync(new() { FullName = "Carol", Age = 9, Grade  = 1 }, null, null);
        }
    }

    [Fact]
    public async Task Filter_NumericLessThan_ReturnsMatchingRows() {
        await SeedAsync();

        var (handler, scope) = _fixture.CreateHandlerWithScope();
        using (scope) {
            var result = await handler.ListAsync(new() { Filter = "age < 20" }, null, null);
            Assert.True(result.IsAllowed());
            var entities = (result.Entities ?? Enumerable.Empty<Student>()).ToList();
            Assert.True(entities.Count >= 1);
            Assert.All(entities, s => Assert.True(s.Age < 20));
        }
    }

    [Fact]
    public async Task Filter_StringWildcard_ReturnsMatchingRows() {
        await SeedAsync();

        var (handler, scope) = _fixture.CreateHandlerWithScope();
        using (scope) {
            var result = await handler.ListAsync(new() { Filter = "full_name = 'A*'" }, null, null);
            Assert.True(result.IsAllowed());
            var entities = (result.Entities ?? Enumerable.Empty<Student>()).ToList();
            Assert.True(entities.Count >= 1);
            Assert.All(entities, s => Assert.StartsWith("A", s.FullName));
        }
    }

    [Fact]
    public async Task Filter_OrExpression_ReturnsBothBranches() {
        await SeedAsync();

        var (handler, scope) = _fixture.CreateHandlerWithScope();
        using (scope) {
            var result = await handler.ListAsync(new() { Filter = "grade = 1 OR grade = 2" }, null, null);
            Assert.True(result.IsAllowed());
            Assert.True(result.TotalSize >= 2);
        }
    }
}
