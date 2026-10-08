using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class TenantMessageContextShould
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Explicit_Invalid_Identity_Cannot_Fall_Back_To_Host(string? value) {
        using var root = new ServiceCollection().BuildServiceProvider();
        var factory = new MessageExecutionScopeFactory(root.GetRequiredService<IServiceScopeFactory>());
        var message = new MessageContext(new Dictionary<string, string?> { [MessageContexts.TenantIdKey] = value });
        await Assert.ThrowsAsync<TenantResolveException>(() => factory.CreateAsync(message).AsTask());
    }

    [Fact]
    public async Task Host_Preparation_Enters_After_Suspension_And_Restores_Parent() {
        using var root = new ServiceCollection().BuildServiceProvider();
        var tenant = new TenantIdentity(Guid.NewGuid());
        using var parent = TenantContext.Enter(tenant);
        var factory = new MessageExecutionScopeFactory(root.GetRequiredService<IServiceScopeFactory>());
        var prepared = await factory.CreateAsync(null);
        await Task.Yield();
        Assert.Equal(tenant, TenantContext.Current);
        using (prepared.Enter()) {
            await using var owned = prepared;
            Assert.Equal(TenantIdentity.Host, TenantContext.Current);
            await Task.Yield();
            Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        }
        Assert.Equal(tenant, TenantContext.Current);
    }
}
