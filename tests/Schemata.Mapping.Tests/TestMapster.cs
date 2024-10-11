using Mapster;
using MapsterMapper;
using Schemata.Mapping.Mapster;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Tests.Models;
using Xunit;

namespace Schemata.Mapping.Tests;

public class TestMapster
{
    [Fact]
    public void Map() {
        var config  = TypeAdapterConfig.GlobalSettings;
        var options = new SchemataMappingOptions();
        options.AddMapping<Source, Destination>(map => {
            map.For(d => d.DisplayName).From(s => (s.Sex == Sex.Male ? "Mr." : "Ms.") + " " + s.Name);
            map.For(d => d.Age).From(s => s.Age).Ignore((s, _) => s.Age < 18);
            map.For(d => d.Grade).Ignore();
            map.For(d => d.Sex).From(s => s.Sex.ToString());
        });

        MapsterConfigurator.Configure(config, options);

        var mapper = new Mapper(config);

        var source = new Source {
            Name  = "John",
            Age   = 18,
            Grade = 10,
            Sex   = Sex.Male,
        };

        var destination = mapper.Map<Destination>(source);

        Assert.Equal("Mr. John", destination.DisplayName);
        Assert.Equal(source.Age, destination.Age);
        Assert.Equal(0, destination.Grade);
        Assert.Equal(nameof(Sex.Male), destination.Sex);
    }
}
