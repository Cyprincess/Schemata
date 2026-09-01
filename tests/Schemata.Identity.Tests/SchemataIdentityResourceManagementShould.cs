using Schemata.Core.Building;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Identity.Skeleton.Entities;
using Xunit;

namespace Schemata.Identity.Tests;

public class SchemataIdentityResourceManagementShould
{
    [Fact]
    public void UseIdentity_Without_ResourceTransport_Does_Not_Register_Management_Resources() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseIdentity());

        using var app = builder.Build();

        Assert.Null(app.Services.GetService<ResourceRegistry>()?.GetResource(typeof(SchemataUser)));
        Assert.Null(app.Services.GetService<ResourceRegistry>()?.GetResource(typeof(SchemataRole)));
    }

    [Fact]
    public void MapHttp_And_MapGrpc_Register_User_And_Role_Management_Resources() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseIdentity().MapHttp().MapGrpc());

        using var app = builder.Build();
        var registry = app.Services.GetRequiredService<ResourceRegistry>();

        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            registry.GetResource(typeof(SchemataUser))!.Endpoints!.OrderBy(endpoint => endpoint, System.StringComparer.Ordinal));
        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            registry.GetResource(typeof(SchemataRole))!.Endpoints!.OrderBy(endpoint => endpoint, System.StringComparer.Ordinal));
    }
}
