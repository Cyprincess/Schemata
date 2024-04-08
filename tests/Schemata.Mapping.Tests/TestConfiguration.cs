using System;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Tests.Models;
using Xunit;

namespace Schemata.Mapping.Tests;

public class TestParser
{
    [Fact]
    public void Map() {
        var options = new SchemataMappingOptions();
        options.AddMapping<Source, Destination>(map => {
            map.For(d => d.DisplayName).From(s => (s.Sex == Sex.Male ? "Mr." : "Ms.") + " " + s.Name);
            map.For(d => d.Sex).From(s => s.Sex.ToString());
        });

        Assert.Equal(2, options.Mappings.Count);
    }

    [Fact]
    public void Ignore() {
        var options = new SchemataMappingOptions();
        options.AddMapping<Source, Destination>(map => {
            map.For(d => d.DisplayName).Ignore();
            map.For(d => d.Sex).From(s => s.Sex.ToString()).Ignore((s, _) => !s.Sex.HasValue);
        });

        Assert.Equal(2, options.Mappings.Count);
    }

    [Fact]
    public void Throw() {
        var options = new SchemataMappingOptions();

        Assert.Throws<InvalidOperationException>(() => options.AddMapping<Source, Destination>(map => {
            map.For(d => d.DisplayName);
        }));
    }
}
