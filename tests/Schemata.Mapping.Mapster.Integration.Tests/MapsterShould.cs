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
                       map.For(d => d.Nickname).Ignore((s, _) => s.Name == "Hidden");
                   });
        });

        var app   = builder.Build();
        var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISimpleMapper>();
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

    [Fact]
    public void Map_FieldWithIgnorePredicate_CompilesWithoutSourceField() {
        var mapper = CreateMapper();

        var result = mapper.Map<Destination>(new Source { Name = "Hidden" });

        Assert.NotNull(result);
    }

    [Fact]
    public void Map_FieldWithIgnorePredicate_AppliesConditionPerInstance() {
        var mapper = CreateMapper();

        // Predicate `s.Name == "Hidden"` leaves hidden nicknames empty and maps visible ones.
        var hidden  = mapper.Map<Destination>(new Source { Name = "Hidden", Nickname  = "stealth" });
        var visible = mapper.Map<Destination>(new Source { Name = "Visible", Nickname = "bright" });

        Assert.Null(hidden!.Nickname);
        Assert.Equal("bright", visible!.Nickname);
    }
}
