using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Services;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class SchemataTenantContextAccessorShould
{
    [Fact]
    public async Task InitializeAsync_Throws_TenantResolveException_When_Tenant_Not_Found() {
        var id = Guid.NewGuid();

        var resolver = new Mock<ITenantResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(id);

        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(m => m.FindByTenantId(id, It.IsAny<CancellationToken>())).ReturnsAsync((SchemataTenant?)null);

        var accessor = new SchemataTenantContextAccessor<SchemataTenant>(
            new ServiceCollection().BuildServiceProvider(),
            resolver.Object,
            manager.Object
        );

        await Assert.ThrowsAsync<TenantResolveException>(() => accessor.InitializeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InitializeAsync_NoOp_When_Resolver_Returns_Null() {
        var resolver = new Mock<ITenantResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var manager = new Mock<ITenantManager<SchemataTenant>>(MockBehavior.Strict);

        var accessor = new SchemataTenantContextAccessor<SchemataTenant>(
            new ServiceCollection().BuildServiceProvider(),
            resolver.Object,
            manager.Object
        );

        await accessor.InitializeAsync(CancellationToken.None);

        Assert.Null(accessor.Tenant);
        manager.VerifyNoOtherCalls();
    }
}
