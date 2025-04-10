using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Xunit;

namespace Schemata.Entity.Tests.TestAdvices;

public class RepositoryCacheShould
{
    [Fact]
    public async Task Query_WithValidStudents_ReturnsCachedResults() {
        var services = new ServiceCollection();

        services.AddRepository(typeof(EfCoreRepository<>))
                .UseEntityFrameworkCore<TestingContext>((_, options) => options.UseInMemoryDatabase("TestingContext"))
                .UseQueryCache();

        var       sp    = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

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
    }
}
