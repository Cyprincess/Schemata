using System;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class RepositoryQueryShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        await _fixture.InitializeAsync();

        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repository.AddAsync(new() {
                                          Uid      = Guid.NewGuid(),
                                          FullName = "Alice",
                                          Age      = 18,
                                          Grade    = 1,
                                          Name     = "q-alice",
                                      });
            await repository.CommitAsync();
        }

        {
            var (repo2, scope2) = _fixture.CreateScopeWithRepository();
            using (scope2) {
                await repo2.AddAsync(new() {
                                         Uid      = Guid.NewGuid(),
                                         FullName = "Bob",
                                         Age      = 19,
                                         Grade    = 2,
                                         Name     = "q-bob",
                                     });
                await repo2.CommitAsync();
            }
        }

        {
            var (repo3, scope3) = _fixture.CreateScopeWithRepository();
            using (scope3) {
                await repo3.AddAsync(new() {
                                         Uid      = Guid.NewGuid(),
                                         FullName = "Charlie",
                                         Age      = 20,
                                         Grade    = 2,
                                         Name     = "q-charlie",
                                     });
                await repo3.CommitAsync();
            }
        }
    }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task EstimateCountAsync_FilteredSqliteQuery_ReturnsNull() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var count = await repository.EstimateCountAsync(q => q.Where(student => student.Grade == 2));

            Assert.Null(count);
        }
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Guid_Keyset_Uses_The_Same_Order_As_The_Database() {
        var ids = new[] {
            Guid.Parse("00000001-0000-0000-0000-000000000000"),
            Guid.Parse("00000100-0000-0000-0000-000000000000"),
            Guid.Parse("00010000-0000-0000-0000-000000000000"),
            Guid.Parse("01000000-0000-0000-0000-000000000000"),
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
        };
        var (writer, writeScope) = _fixture.CreateScopeWithRepository();
        using (writeScope) {
            foreach (var uid in ids) {
                await writer.AddAsync(new() { Uid = uid, Name = $"guid-{uid:N}" });
            }
            await writer.CommitAsync();
        }

        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            var ordered = await repository.ListAsync(q => q.Where(row => ids.Contains(row.Uid))
                                                          .OrderBy(row => row.Uid).Select(row => row.Uid)).ToListAsync();
            var cursor = ordered[1];
            var remaining = await repository.ListAsync(q => q.Where(row => ids.Contains(row.Uid) && row.Uid.CompareTo(cursor) > 0)
                                                            .OrderBy(row => row.Uid).Select(row => row.Uid)).ToListAsync();
            Assert.Equal(ordered.Skip(2), remaining);
        }
    }
}
