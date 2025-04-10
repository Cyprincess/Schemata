using System;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Tests.Models;
using Xunit;

namespace Schemata.Mapping.Tests;

public class MappingConfigurationShould
{
    [Fact]
    public void AddMapping_WithValidMapping_AddsMappingToOptions() {
        var options = new SchemataMappingOptions();
        options.AddMapping<Source, Destination>(map => {
            map.For(d => d.DisplayName).From(s => (s.Sex == Sex.Male ? "Mr." : "Ms.") + " " + s.Name);
            map.For(d => d.Sex).From(s => s.Sex.ToString());
        });

        Assert.NotNull(options.Mappings);
        Assert.Equal(2, options.Mappings.Count);
    }

    [Fact]
    public void AddMapping_WithIgnoreMapping_AddsMappingToOptions() {
        var options = new SchemataMappingOptions();
        options.AddMapping<Source, Destination>(map => {
            map.For(d => d.DisplayName).Ignore();
            map.For(d => d.Sex).From(s => s.Sex.ToString()).Ignore((s, _) => !s.Sex.HasValue);
        });

        Assert.NotNull(options.Mappings);
        Assert.Equal(2, options.Mappings.Count);
    }

    [Fact]
    public void AddMapping_WithInvalidMapping_ThrowsException() {
        var options = new SchemataMappingOptions();

        Assert.Throws<InvalidOperationException>(() => {
            options.AddMapping<Source, Destination>(map => {
                map.For(d => d.DisplayName);
            });
        });
    }
}
