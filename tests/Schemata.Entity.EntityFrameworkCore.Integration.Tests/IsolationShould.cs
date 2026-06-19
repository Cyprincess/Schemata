using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class IsolationShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task TwoRepositoriesInSameScope_HaveIndependentChangeTrackers() {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var       a     = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var       b     = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();

        await a.AddAsync(new() {
                             FullName = "Iso-Alice",
                             Age      = 18,
                             Grade    = 1,
                             Name     = "iso-alice",
        });

        // Repository b commits its empty tracker while repository a keeps its pending insert isolated.
        await b.CommitAsync();
        Assert.Equal(0, await b.CountAsync(q => q.Where(s => s.Name == "iso-alice")));

        await a.CommitAsync();

        using var verifyScope = _fixture.ServiceProvider.CreateScope();
        var       verify      = verifyScope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        Assert.Equal(1, await verify.CountAsync(q => q.Where(s => s.Name == "iso-alice")));
    }

    [Fact]
    public async Task EnumerateOnOneRepo_ConcurrentWriteOnAnother_DoesNotThrow() {
        {
            using var seedScope = _fixture.ServiceProvider.CreateScope();
            var       seedRepo  = seedScope.ServiceProvider.GetRequiredService<IRepository<Student>>();
            for (var i = 0; i < 5; i++) {
                await seedRepo.AddAsync(new() {
                                            FullName = $"Seed-{i}",
                                            Age      = 18 + i,
                                            Grade    = 1,
                                            Name     = $"seed-{i}",
                });
            }

            await seedRepo.CommitAsync();
        }

        using var scope  = _fixture.ServiceProvider.CreateScope();
        var       reader = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var       writer = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();

        await foreach (var student in reader.ListAsync<Student>(q => q.OrderBy(s => s.Name))) {
            await writer.AddAsync(new() {
                                      FullName = $"Side-{student.Name}",
                                      Age      = 30,
                                      Grade    = 9,
                                      Name     = $"side-{student.Name}",
                                  });
        }

        await writer.CommitAsync();

        using var verifyScope = _fixture.ServiceProvider.CreateScope();
        var       verify      = verifyScope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var       count       = await verify.CountAsync(q => q.Where(s => s.Name!.StartsWith("side-")));
        Assert.Equal(5, count);
    }
}
