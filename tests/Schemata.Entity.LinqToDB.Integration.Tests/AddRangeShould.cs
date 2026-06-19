using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Common;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class AddRangeShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task AddRange_PersistsAllEntitiesAfterCommit() {
        var batch = Batch("bulk", 5);

        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repository.AddRangeAsync(batch);
            await repository.CommitAsync();
        }

        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
                foreach (var student in batch) {
                    var found = await verifier.FirstOrDefaultAsync<Student>(q => q.Where(s => s.Uid == student.Uid));
                    Assert.NotNull(found);
                    Assert.Equal(student.Name, found!.Name);
                }
            }
        }
    }

    [Fact]
    public async Task AddRange_RunsAddAdvisorsPerEntity() {
        var batch = Batch("advised", 3);

        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repository.AddRangeAsync(batch);
            await repository.CommitAsync();
        }

        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
                foreach (var student in batch) {
                    var found = await verifier.FirstOrDefaultAsync<Student>(q => q.Where(s => s.Uid == student.Uid));
                    // The add-concurrency advisor stamps a token per entity before persistence.
                    Assert.NotEqual(Guid.Empty, found!.Timestamp);
                }
            }
        }
    }

    [Fact]
    public async Task AddRange_WithoutCommit_RollsBack() {
        var batch = Batch("rollback", 3);

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                await repository.AddRangeAsync(batch);
                // Scope disposal rolls back the staged bulk insert.
            }
        }

        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
                foreach (var student in batch) {
                    var found = await verifier.FirstOrDefaultAsync<Student>(q => q.Where(s => s.Uid == student.Uid));
                    Assert.Null(found);
                }
            }
        }
    }

    private static List<Student> Batch(string prefix, int count) {
        var batch = new List<Student>();
        for (var i = 0; i < count; i++) {
            batch.Add(new() {
                          Uid      = Identifiers.NewUid(),
                          FullName = $"{prefix}-{i}",
                          Name     = $"{prefix}-{i}",
                          Age      = 20 + i,
                          Grade    = 1,
                      });
        }

        return batch;
    }
}
