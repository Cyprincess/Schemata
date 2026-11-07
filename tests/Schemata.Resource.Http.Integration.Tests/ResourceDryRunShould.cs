using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Layer", "Integration")]
[Trait("Category", "Integration")]
public class ResourceDryRunShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task Create_ValidateOnly_ReturnsNoContent_AndPersistsNothing() {
        var fullName = $"HttpDryRun{Guid.NewGuid():N}";
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/v1/students",
            new StringContent($$"""{"full_name":"{{fullName}}","validate_only":true}""",
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<TestDbContext>>()
                                          .CreateDbContextAsync();
        Assert.False(await db.Students.AnyAsync(row => row.FullName == fullName));
    }
}
