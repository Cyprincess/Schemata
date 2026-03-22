using System.Linq;
using System.Threading.Tasks;
using Schemata.Resource.Foundation.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Integration.Tests;

public class ResourceCrudIntegrationShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task Create_ThenFindByName_ReturnsSameEntity() {
        var (handler, scope) = _fixture.CreateHandlerWithScope();
        using (scope) {
            var request = new Student { FullName = "Alice", Age = 18, Grade = 1 };
            var created = await handler.CreateAsync(request, null, null);
            Assert.True(created.IsAllowed());
            Assert.NotNull(created.Detail?.Name);

            var entity = await handler.FindByNameAsync(created.Detail!.Name, null);
            Assert.NotNull(entity);
            Assert.Equal("Alice", entity.FullName);
        }
    }

    [Fact]
    public async Task Create_ThenList_ContainsCreatedEntity() {
        var (handler, scope) = _fixture.CreateHandlerWithScope();
        using (scope) {
            await handler.CreateAsync(new() { FullName = "Bob", Age = 19, Grade = 2 }, null, null);

            var list = await handler.ListAsync(new(), null, null);
            Assert.True(list.TotalSize >= 1);
            Assert.Contains(list.Entities ?? Enumerable.Empty<Student>(), s => s.FullName == "Bob");
        }
    }

    [Fact]
    public async Task Update_ChangesStoredField() {
        string name;
        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                var created = await handler.CreateAsync(new() { FullName = "Charlie", Age = 20, Grade = 2 }, null,
                                                        null);
                Assert.NotNull(created.Detail?.Name);
                name = created.Detail!.Name!;
            }
        }

        // Fetch and update in the SAME scope so EF uses one DbContext and avoids
        // cross-context identity-map conflicts from AdviceUpdateConcurrency loading
        // the stored entity into the same DbContext that owns the fetched entity.
        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                var entity = await handler.FindByNameAsync(name, null);
                Assert.NotNull(entity);

                // Use UpdateMask so only FullName is copied; preserves entity.Id on entity.
                var updated = await handler.UpdateAsync(new() { FullName = "Charlie Updated", UpdateMask = "FullName" },
                                                        entity, null, null);
                Assert.True(updated.IsAllowed());
            }
        }

        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                var fetched = await handler.FindByNameAsync(name, null);
                Assert.Equal("Charlie Updated", fetched?.FullName);
            }
        }
    }

    [Fact]
    public async Task Delete_RemovesEntityFromStore() {
        string name;
        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                var created = await handler.CreateAsync(new() { FullName = "Dave", Age = 21, Grade = 3 }, null, null);
                Assert.NotNull(created.Detail?.Name);
                name = created.Detail!.Name!;
            }
        }

        Student entity;
        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                entity = (await handler.FindByNameAsync(name, null))!;
                Assert.NotNull(entity);
            }
        }

        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                await handler.DeleteAsync(entity, null, false, null, null);
            }
        }

        {
            var (handler, scope) = _fixture.CreateHandlerWithScope();
            using (scope) {
                var fetched = await handler.FindByNameAsync(name, null);
                Assert.Null(fetched);
            }
        }
    }
}
