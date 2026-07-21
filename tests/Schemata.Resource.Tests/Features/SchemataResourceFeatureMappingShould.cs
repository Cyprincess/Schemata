using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Features;

public class SchemataResourceFeatureMappingShould
{
    [Fact]
    public void BuildHost_Succeeds_WhenNoMappingProviderRegistered() {
        using var app = BuildHostWithoutMappingProvider();

        Assert.Null(app.Services.GetService<ISimpleMapper>());
    }

    [Fact]
    public void ResolveHandler_ThrowsISimpleMapperError_WhenNoMappingProviderRegistered() {
        using var app   = BuildHostWithoutMappingProvider();
        using var scope = app.Services.CreateScope();

        var ex = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ResourceOperationHandler<Student, Student, Student, Student>>());

        Assert.Contains("ISimpleMapper", ex.Message, StringComparison.Ordinal);
    }

    private static WebApplication BuildHostWithoutMappingProvider() {
        var builder = WebApplication.CreateBuilder();

        builder.UseSchemata(schema => {
            var resource = schema.UseResource();
            resource.Use<Student>();
            schema.Services.AddScoped(_ => Mock.Of<IRepository<Student>>());
        });

        return builder.Build();
    }
}
