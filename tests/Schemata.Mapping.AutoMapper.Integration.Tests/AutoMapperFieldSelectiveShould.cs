using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Mapping.AutoMapper.Integration.Tests.Fixtures;
using Schemata.Mapping.Skeleton;
using Xunit;

namespace Schemata.Mapping.AutoMapper.Integration.Tests;

[Trait("Category", "Integration")]
public class AutoMapperFieldSelectiveShould
{
    private static ISimpleMapper CreateMapper() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
            schema.UseAutoMapper()
                  .Map<Source, Destination>(map => {
                       map.For(d => d.DisplayName)
                          .From(s => (s.Sex == Sex.Male ? "Mr." : "Ms.") + " " + s.Name);
                       map.For(d => d.Sex).From(s => s.Sex.ToString());
                   });
        });

        var app   = builder.Build();
        var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISimpleMapper>();
    }

    [Fact]
    public void Map_WithFieldList_CopiesOnlyNamedFields() {
        var mapper = CreateMapper();

        var source = new Source {
            Name  = "Alice",
            Age   = 30,
            Grade = 10,
            Sex   = Sex.Female,
        };

        var destination = new Destination {
            DisplayName = "Original",
            Age         = 0,
            Grade       = 0,
            Sex         = "Unknown",
        };

        mapper.Map(source, destination, new List<string> { "Age" });

        Assert.Equal(30, destination.Age);
        Assert.Equal("Original", destination.DisplayName);
        Assert.Equal("Unknown", destination.Sex);
        Assert.Equal(0, destination.Grade);
    }

    [Fact]
    public void Map_WithFieldList_LeavesUnlistedFieldsUnchanged() {
        var mapper = CreateMapper();

        var source = new Source {
            Name  = "Bob",
            Age   = 25,
            Grade = 8,
            Sex   = Sex.Male,
        };

        var destination = new Destination {
            DisplayName = "Existing Name",
            Age         = 99,
            Grade       = 77,
            Sex         = "PresetSex",
        };

        mapper.Map(source, destination, new List<string> { "DisplayName", "Sex" });

        Assert.Equal("Mr. Bob", destination.DisplayName);
        Assert.Equal(nameof(Sex.Male), destination.Sex);
        Assert.Equal(99, destination.Age);
        Assert.Equal(77, destination.Grade);
    }
}
