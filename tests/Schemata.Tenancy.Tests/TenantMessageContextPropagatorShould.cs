using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
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
    public async Task Host_Preparation_Restores_Against_The_Final_Provider() {
        var scopes = new Mock<ITenantServiceScopeFactory<SchemataTenant>>();
        using var root = new ServiceCollection().AddSingleton(scopes.Object)
            .AddScoped(typeof(TenantIdentity), _ => TenantIdentity.Host)
            .AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>().BuildServiceProvider();
        scopes.Setup(value => value.CreateAsync(TenantIdentity.Host, default)).Returns(async () => {
            await Task.Yield();
            return root.CreateAsyncScope();
        });
        var factory = new TenantMessageExecutionScopeFactory<SchemataTenant>(root.GetRequiredService<IServiceScopeFactory>());
        await using var prepared = await factory.CreateAsync(null);
        await prepared.RestoreAsync(new(new Dictionary<string, string?>()));
        Assert.Equal(TenantIdentity.Host, prepared.Services.GetRequiredService<TenantIdentity>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_And_Restore_Use_Explicit_Source_And_Target_Providers(bool host) {
        var identity = host ? TenantIdentity.Host : new TenantIdentity(Guid.NewGuid());
        using var source = new ServiceCollection().AddScoped(typeof(TenantIdentity), _ => identity)
            .AddSingleton<IMessageContextPropagator, TenantMessageContextPropagator>().BuildServiceProvider();
        var captured = MessageContexts.Capture(source);
        Assert.Equal(identity.Uid?.ToString("D") ?? "host", captured.Items[TenancyConstants.Messaging.TenantIdKey]);
        var propagator = source.GetRequiredService<IMessageContextPropagator>();
        await propagator.RestoreAsync(captured.Items, source);

        var other = new TenantIdentity(Guid.NewGuid());
        using var target = new ServiceCollection().AddScoped(typeof(TenantIdentity), _ => other).BuildServiceProvider();
        await Assert.ThrowsAsync<TenantResolveException>(() => propagator.RestoreAsync(captured.Items, target).AsTask());
        Assert.Equal(other, target.GetRequiredService<TenantIdentity>());
    }
}
