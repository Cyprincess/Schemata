using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Moq;
using Xunit;

namespace Schemata.Tenancy.Tests;

[Trait("Layer", "Integration")]
public class TenantMessageContextPropagatorShould
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Explicit_Invalid_Identity_Cannot_Fall_Back_To_Host(string? value) {
        using var root = new ServiceCollection().BuildServiceProvider();
        var factory = new TenantMessageExecutionScopeFactory<SchemataTenant>(root.GetRequiredService<IServiceScopeFactory>());
        var message = new MessageContext(new Dictionary<string, string?> { [TenancyConstants.Messaging.TenantIdKey] = value });
        await Assert.ThrowsAsync<TenantResolveException>(() => factory.CreateAsync(message).AsTask());
    }

    [Fact]
    public async Task Host_Preparation_Enters_After_Suspension_And_Restores_Parent() {
        var scopes = new Mock<ITenantServiceScopeFactory<SchemataTenant>>();
        using var root = new ServiceCollection().AddSingleton(scopes.Object)
            .AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>().BuildServiceProvider();
        scopes.Setup(value => value.CreateAsync(TenantIdentity.Host, default)).Returns(async () => {
            await Task.Yield();
            return root.CreateAsyncScope();
        });
        var tenant = new TenantIdentity(Guid.NewGuid());
        using var parent = TenantContext.Enter(tenant);
        var factory = new TenantMessageExecutionScopeFactory<SchemataTenant>(root.GetRequiredService<IServiceScopeFactory>());
        var prepared = await factory.CreateAsync(null);
        await Task.Yield();
        Assert.Equal(tenant, TenantContext.Current);
        await using (prepared) {
            using (prepared.Enter()) {
                Assert.Equal(TenantIdentity.Host, TenantContext.Current);
                await prepared.RestoreAsync(new(new Dictionary<string, string?>()));
                await Task.Yield();
                Assert.Equal(TenantIdentity.Host, TenantContext.Current);
            }
            Assert.Equal(tenant, TenantContext.Current);
            using (prepared.Enter()) Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        }
        Assert.Equal(tenant, TenantContext.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_And_Restore_Validate_The_Caller_Entered_Identity(bool host) {
        using var root = new ServiceCollection().AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>().BuildServiceProvider();
        var identity = host ? TenantIdentity.Host : new TenantIdentity(Guid.NewGuid());
        MessageContext captured;
        using (TenantContext.Enter(identity)) captured = MessageContexts.Capture(root);
        Assert.Equal(identity.Uid?.ToString("D") ?? "host", captured.Items[TenancyConstants.Messaging.TenantIdKey]);
        var propagator = root.GetRequiredService<IMessageContextPropagator>();
        using (TenantContext.Enter(identity)) {
            await propagator.RestoreAsync(captured.Items, root);
            Assert.Equal(identity, TenantContext.Current);
        }
        var other = new TenantIdentity(Guid.NewGuid());
        using (TenantContext.Enter(other)) {
            await Assert.ThrowsAsync<TenantResolveException>(() => propagator.RestoreAsync(captured.Items, root).AsTask());
            Assert.Equal(other, TenantContext.Current);
        }
    }
}
