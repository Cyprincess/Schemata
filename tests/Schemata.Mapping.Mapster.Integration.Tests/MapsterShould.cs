using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Mapping.Mapster.Integration.Tests.Fixtures;
using Schemata.Mapping.Skeleton;
using Xunit;

namespace Schemata.Mapping.Mapster.Integration.Tests;

[Trait("Category", "Integration")]
public class MapsterShould
{
    private static ISimpleMapper CreateMapper() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
                schema.UseMapster()
                      .Map<Source, Destination>(map => {
                               map.For(d => d.DisplayName)
                                  .From(s => (s.Sex == Sex.Male ? "Mr." : "Ms.") + " " + s.Name);
                               map.For(d => d.Sex).From(s => s.Sex.ToString());
                               map.For(d => d.Grade).Ignore();
                           }
                       );
            }
        );

        var app   = builder.Build();
        var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISimpleMapper>();
    }

    [Fact]
    public void Map_BasicFields_CopiesMatchingProperties() {
        var mapper = CreateMapper();

        var source = new Source {
            Name  = "Alice",
            Age   = 25,
            Grade = 10,
            Sex   = Sex.Female,
        };

        var result = mapper.Map<Destination>(source);

        Assert.NotNull(result);
        Assert.Equal(source.Age, result.Age);
    }

    [Fact]
    public void Map_CustomConverter_AppliesTransformation() {
        var mapper = CreateMapper();

        var male = new Source {
            Name  = "John",
            Age   = 30,
            Grade = 5,
            Sex   = Sex.Male,
        };

        var female = new Source {
            Name  = "Jane",
            Age   = 28,
            Grade = 3,
            Sex   = Sex.Female,
        };

        var maleResult   = mapper.Map<Destination>(male);
        var femaleResult = mapper.Map<Destination>(female);

        Assert.NotNull(maleResult);
        Assert.Equal("Mr. John", maleResult.DisplayName);
        Assert.Equal(nameof(Sex.Male), maleResult.Sex);

        Assert.NotNull(femaleResult);
        Assert.Equal("Ms. Jane", femaleResult.DisplayName);
        Assert.Equal(nameof(Sex.Female), femaleResult.Sex);
    }

    [Fact]
    public void Map_IgnoredField_YieldsDefault() {
        var mapper = CreateMapper();

        var source = new Source {
            Name  = "Bob",
            Age   = 22,
            Grade = 12,
            Sex   = Sex.Male,
        };

        var result = mapper.Map<Destination>(source);

        Assert.NotNull(result);
        Assert.Equal(0, result.Grade);
    }
}
