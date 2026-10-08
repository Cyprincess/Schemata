using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class ResourceNameOwnershipShould
{
    [Fact]
    public async Task Persist_And_Deduplicate_Slots_By_Key_Independent_Of_Consumer_Name() {
        using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        using var ambient = AdviceContext.Establish(new(scope.ServiceProvider));
        var store = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        var first = await store.GetOrCreateAsync(null, "test-slots", "nonce", "first", TimeSpan.FromMinutes(5));
        var second = await store.GetOrCreateAsync(null, "test-slots", "nonce", "second", TimeSpan.FromMinutes(5));
        Assert.Equal(first.Uid, second.Uid);
        Assert.Equal("first", second.Value);
        Assert.Equal("nonce", second.Key);
        Assert.StartsWith("resource-", second.Name);
        Assert.NotEqual(second.Key, second.Name);
        Assert.Equal("tokens/" + second.Name, second.CanonicalName);
        var explicitName = await store.CreateAsync(new() { Name = "caller-supplied", Provider = "test-slots", Key = "explicit" });
        Assert.Equal("caller-supplied", explicitName!.Name);
        Assert.Equal("tokens/caller-supplied", explicitName.CanonicalName);
        await store.SetAsync(null, "test-slots", "nonce", "updated", null);
        Assert.Equal("updated", (await store.GetAsync(null, "test-slots", "nonce"))!.Value);
        await store.RemoveAsync(null, "test-slots", "nonce");
        Assert.Null(await store.GetAsync(null, "test-slots", "nonce"));
    }

    [Fact]
    public async Task Preserve_Caller_Name_While_Rejecting_Unnamed_Rows_Without_Consumer_Advisor() {
        using var factory = new WebAppFactory().WithServices(services => {
            var advisor = services.Single(descriptor => descriptor.ImplementationType == typeof(ResourceNameAdvisor<SchemataToken>));
            services.Remove(advisor);
        });
        using var scope = factory.Services.CreateScope();
        using var ambient = AdviceContext.Establish(new(scope.ServiceProvider));
        var store = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        var named = await store.CreateAsync(new() { Name = "caller-token", Key = "caller-key", Provider = "test-slots" });
        Assert.Equal("caller-token", named!.Name);
        Assert.Equal("tokens/caller-token", named.CanonicalName);
        await Assert.ThrowsAsync<ValidationException>(() => store.CreateAsync(new() { Key = "unnamed-key" }));
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await db.CreateDbContextAsync();
        Assert.False(await context.Tokens.AnyAsync(token => token.Key == "unnamed-key"));
    }
}
