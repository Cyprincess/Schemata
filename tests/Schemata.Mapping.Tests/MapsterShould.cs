using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Tests.Models;
using Xunit;

namespace Schemata.Mapping.Tests;

public class MapsterShould
{
    [Fact]
    public void Map_WithValidSource_MapsToDestinationWithCorrectValues() {
        var builder = WebApplication.CreateBuilder()
                                    .UseSchemata(schema => {
                                         schema.UseMapster()
                                               .Map<Source, Destination>(map => {
                                                    map.For(d => d.DisplayName).From(s => (s.Sex == Sex.Male ? "Mr." : "Ms.") + " " + s.Name);
                                                    map.For(d => d.Age).From(s => s.Age).Ignore((s, _) => s.Age < 18);
                                                    map.For(d => d.Grade).Ignore();
                                                    map.For(d => d.Sex).From(s => s.Sex.ToString());
                                                });
                                     });

        var app = builder.Build();

        using var scope = app.Services.CreateScope();

        var mapper = scope.ServiceProvider.GetRequiredService<ISimpleMapper>();

        var source = new Source {
            Name  = "John",
            Age   = 18,
            Grade = 10,
            Sex   = Sex.Male,
        };

        var destination = mapper.Map<Destination>(source);
        Assert.NotNull(destination);
        Assert.Equal("Mr. John", destination.DisplayName);
        Assert.Equal(source.Age, destination.Age);
        Assert.Equal(0, destination.Grade);
        Assert.Equal(nameof(Sex.Male), destination.Sex);
    }
}
