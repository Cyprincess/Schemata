using System.Linq;
using System.Threading.Tasks;
using Schemata.Resource.Foundation.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Integration.Tests;

public class ResourcePaginationIntegrationShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task Pagination_TwoPages_CoverAllItems() {
        // Seed 5 students
        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                for (var i = 1; i <= 5; i++) {
                    await handler.CreateAsync(new() { FullName = $"Student{i}", Age = 18 + i }, null, null);
                }
            }
        }

        string? nextToken;
        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                var page1 = await handler.ListAsync(new() { PageSize = 3 }, null, null);
                Assert.True(page1.IsAllowed());
                Assert.Equal(3, (page1.Entities ?? Enumerable.Empty<Student>()).Count());
                Assert.NotNull(page1.NextPageToken);
                nextToken = page1.NextPageToken;
            }
        }

        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                var page2 = await handler.ListAsync(new() { PageSize = 3, PageToken = nextToken }, null, null);
                Assert.True(page2.IsAllowed());
                Assert.Equal(2, (page2.Entities ?? Enumerable.Empty<Student>()).Count());
                Assert.Null(page2.NextPageToken);
            }
        }
    }
}
