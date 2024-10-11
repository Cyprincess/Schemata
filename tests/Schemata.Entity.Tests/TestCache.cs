using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Tests.Entity;
using Xunit;

namespace Schemata.Entity.Tests;

public class TestCache
{
    [Fact]
    public async Task Test() {
        var services = new ServiceCollection();

        services.AddRepository(typeof(EfCoreRepository<>))
                .UseEntityFrameworkCore<TestingContext>((_, options) => options.UseInMemoryDatabase("TestingContext"))
                .UseQueryCache();

        var sp    = services.BuildServiceProvider();
        var scope = sp.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TestingContext>();

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var name = "Alice";

        context.AddRange(new Student { Name = name },
                         new Student {
                             Name = "Bob",
                             Age  = 21,
                         });

        await context.SaveChangesAsync();

        var repository = new EntityFrameworkCoreRepository<TestingContext, Student>(scope.ServiceProvider, context);

        var alice = await repository.SingleOrDefaultAsync(q => q.Where(u => u.Name!.Contains("Alice")).OrderByDescending(u => u.Name));
        var bob = await repository.SingleOrDefaultAsync(q => q.Where(u => u.Name!.Contains("Bob") && u.Age > 20));

        await repository.UpdateAsync(bob!);
    }
}
