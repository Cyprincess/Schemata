using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Tests.TestAdvices;

public class RepositoryCacheShould
{
    [Fact]
    public async Task Query_WithValidStudents_ReturnsCachedResults() {
        var services = new ServiceCollection();

        services.AddRepository(typeof(EfCoreRepository<>))
                .UseEntityFrameworkCore<TestingContext>((_, options) => options.UseSqlite("DataSource=file::memory:?cache=shared"))
                .UseQueryCache();

        var       sp    = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TestingContext>();

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        context.AddRange(new Student {
                             Id   = 1,
                             Name = "Alice",
                         },
                         new Student {
                             Id   = 2,
                             Name = "Bob",
                             Age  = 21,
                         });

        await context.SaveChangesAsync();

        var repository = new EntityFrameworkCoreRepository<TestingContext, Student>(scope.ServiceProvider, context);

        var alice = await repository.SingleOrDefaultAsync(q => q.Where(u => u.Name!.Contains("Alice")).OrderByDescending(u => u.Name));
        Assert.NotNull(alice);
        Assert.Equal("Alice", alice.Name);

        var bob = await repository.SingleOrDefaultAsync(q => q.Where(u => u.Name!.Contains("Bob") && u.Age > 20));
        Assert.NotNull(bob);
        Assert.Equal("Bob", bob.Name);
        Assert.Equal(21, bob.Age);

        repository.Detach(bob);

        await repository.UpdateAsync(new() {
            Id = bob.Id,
            Name = bob.Name,
            Age = 22,
        });

        var cached = await repository.SingleOrDefaultAsync(q => q.Where(u => u.Name!.Contains("Bob") && u.Age > 20));
        Assert.NotNull(cached);
        Assert.Equal(bob.Name, cached.Name);
        Assert.Equal(bob.Age, cached.Age);

        var fresh = await repository.Once().SuppressQueryCache().SingleOrDefaultAsync(q => q.Where(u => u.Name!.Contains("Bob") && u.Age > 20));
        Assert.NotNull(fresh);
        Assert.Equal(bob.Name, fresh.Name);
        Assert.Equal(22, fresh.Age);
    }
}
