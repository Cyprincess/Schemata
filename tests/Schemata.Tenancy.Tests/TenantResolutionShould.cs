using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Tenancy.Foundation.Resolvers;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class TenantResolutionShould
{
    [Fact]
    public async Task Request_And_Authenticated_Principal_Must_Agree() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Request.Headers["x-tenant-id"] = a.ToString();
        var context = new HttpContextAccessor { HttpContext = http };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid uid, CancellationToken _) => new SchemataTenant { Uid = uid });
        using var root = new ServiceCollection().BuildServiceProvider();
        var accessor = new SchemataTenantContextAccessor<SchemataTenant>(root,
            [new RequestHeaderResolver(context), new RequestPrincipalResolver(context)], manager.Object);
        await accessor.InitializeAsync(TenantResolutionStage.Request, default);
        http.User = new(new ClaimsIdentity([new Claim("Tenant", b.ToString())]));
        await accessor.InitializeAsync(TenantResolutionStage.Principal, default);
        Assert.Equal(a, accessor.Tenant!.Uid);
        http.User = new(new ClaimsIdentity([new Claim("Tenant", b.ToString())], "authenticated"));
        await Assert.ThrowsAsync<TenantResolveException>(() => accessor.InitializeAsync(TenantResolutionStage.Principal, default));
    }

    [Fact]
    public async Task Principal_Only_Selection_Is_Deferred_Until_Authenticated_Stage() {
        var uid = Guid.NewGuid();
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim("Tenant", uid.ToString())], "authenticated")) };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(uid, It.IsAny<CancellationToken>())).ReturnsAsync(new SchemataTenant { Uid = uid });
        using var root = new ServiceCollection().BuildServiceProvider();
        var accessor = new SchemataTenantContextAccessor<SchemataTenant>(root,
            [new RequestPrincipalResolver(new HttpContextAccessor { HttpContext = http })], manager.Object);
        await accessor.InitializeAsync(TenantResolutionStage.Request, default);
        Assert.Null(accessor.Tenant);
        await accessor.InitializeAsync(TenantResolutionStage.Principal, default);
        Assert.Equal(uid, accessor.Tenant!.Uid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Explicit_Invalid_Header_Fails_Alongside_An_Unmatched_Host(string value) {
        var http = new DefaultHttpContext();
        http.Request.Host = new("unmatched.test");
        http.Request.Headers["x-tenant-id"] = value;
        var context = new HttpContextAccessor { HttpContext = http };
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        using var root = new ServiceCollection().BuildServiceProvider();
        var accessor = new SchemataTenantContextAccessor<SchemataTenant>(root,
            [new RequestHostResolver<SchemataTenant>(context, manager.Object), new RequestHeaderResolver(context)], manager.Object);
        await Assert.ThrowsAsync<TenantResolveException>(() => accessor.InitializeAsync(default));
    }
}
