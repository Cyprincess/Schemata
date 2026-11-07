using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Security.Foundation.Extensions;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Security.Tests;

[Trait("Layer", "Unit")]
public class TokenStoreRegistrationShould
{
    [Fact]
    public void Resolve_An_Unknown_Keyed_Store_Fails() {
        using var provider = new ServiceCollection()
                            .AddSingleton(TimeProvider.System)
                            .AddTokenStores<SchemataToken>()
                            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredKeyedService<ITokenStore<SchemataToken>>("unknown"));
    }
}
