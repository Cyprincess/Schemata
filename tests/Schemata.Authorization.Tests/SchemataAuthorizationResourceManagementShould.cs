using Schemata.Core.Building;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Xunit;

namespace Schemata.Authorization.Tests;

public class SchemataAuthorizationResourceManagementShould
{
    [Fact]
    public void UseAuthorization_Without_ResourceTransport_Does_Not_Register_Management_Resources() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseAuthorization(options => Configure(options)));

        using var app = builder.Build();

        Assert.Null(app.Services.GetService<ResourceRegistry>()?.GetResource(typeof(SchemataApplication)));
        Assert.Null(app.Services.GetService<ResourceRegistry>()?.GetResource(typeof(SchemataScope)));
        Assert.Null(app.Services.GetService<ResourceRegistry>()?.GetResource(typeof(SchemataToken)));
    }

    [Fact]
    public void MapHttp_And_MapGrpc_Register_Application_Scope_And_Token_Management_Resources() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseAuthorization(options => Configure(options)).MapHttp().MapGrpc());

        using var app = builder.Build();
        var registry = app.Services.GetRequiredService<ResourceRegistry>();

        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            registry.GetResource(typeof(SchemataApplication))!.Endpoints!.OrderBy(endpoint => endpoint, System.StringComparer.Ordinal));
        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            registry.GetResource(typeof(SchemataScope))!.Endpoints!.OrderBy(endpoint => endpoint, System.StringComparer.Ordinal));
        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            registry.GetResource(typeof(SchemataToken))!.Endpoints!.OrderBy(endpoint => endpoint, System.StringComparer.Ordinal));
    }

    private static void Configure(Foundation.Authentication.SchemataAuthorizationOptions options) {
        options.Issuer = "https://issuer.example";
    }
}
