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
                  .Map<SourceAddress, DestinationAddress>(map => {
                       map.For(d => d.City).From(s => s.City);
                       map.For(d => d.Zip).From(s => s.Zip);
                   })
                  .Map<SourceSettings, DestinationSettings>(map => {
                       map.For(d => d.Enabled).From(s => s.Enabled);
                       map.For(d => d.Level).From(s => s.Level);
                   })
                  .Map<SourceProfile, DestinationProfile>(map => {
                       map.For(d => d.DisplayName).From(s => s.DisplayName);
                       map.For(d => d.Bio).From(s => s.Bio);
                       map.For(d => d.Locale).From(s => s.Locale);
                   })
                  .Map<Source, Destination>(map => {
                       map.For(d => d.DisplayName)
                          .From(s => (s.Sex == Sex.Male ? "Mr." : "Ms.") + " " + s.Name);
                       map.For(d => d.Sex).From(s => s.Sex.ToString());
                       map.For(d => d.Title).From(s => s.Name);
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

    [Fact]
    public void Map_WithNestedFieldList_CopiesOnlyNestedLeaf() {
        var mapper = CreateMapper();

        var source = new Source {
            Age = 30, Profile = new() { DisplayName = "New Name", Bio = "New Bio", Locale = "fr" },
        };

        var destination = new Destination {
            Age = 99, Profile = new() { DisplayName = "Old Name", Bio = "Old Bio", Locale = "en" },
        };

        mapper.Map(source, destination, new List<string> { "Profile.DisplayName" });

        Assert.Equal(99, destination.Age);
        Assert.NotNull(destination.Profile);
        Assert.Equal("New Name", destination.Profile.DisplayName);
        Assert.Equal("Old Bio", destination.Profile.Bio);
        Assert.Equal("en", destination.Profile.Locale);
    }

    [Fact]
    public void Map_WithNestedFieldList_NullTarget_PopulatesOnlyMaskedLeaf() {
        var mapper = CreateMapper();

        var source = new Source {
            Age = 30, Profile = new() { DisplayName = "New Name", Bio = "New Bio", Locale = "fr" },
        };

        var destination = new Destination { Age = 99, Profile = null };

        mapper.Map(source, destination, new List<string> { "Profile.DisplayName" });

        Assert.Equal(99, destination.Age);
        Assert.NotNull(destination.Profile);
        Assert.Equal("New Name", destination.Profile.DisplayName);
        // A newly created nested destination keeps unmasked fields empty.
        Assert.Null(destination.Profile.Bio);
        Assert.Null(destination.Profile.Locale);
    }

    [Fact]
    public void Map_WithNestedFieldList_ClearsMaskedNullLeaf() {
        var mapper = CreateMapper();

        var source = new Source { Profile = new() { DisplayName = null, Bio = "New Bio", Locale = "fr" } };

        var destination = new Destination {
            Profile = new() { DisplayName = "Old Name", Bio = "Old Bio", Locale = "en" },
        };

        mapper.Map(source, destination, new List<string> { "Profile.DisplayName" });

        Assert.NotNull(destination.Profile);
        Assert.Null(destination.Profile.DisplayName);
        Assert.Equal("Old Bio", destination.Profile.Bio);
        Assert.Equal("en", destination.Profile.Locale);
    }

    [Fact]
    public void Map_WithFieldList_Keeps_Unmasked_Mutable_Object_Intact() {
        var mapper = CreateMapper();

        var source = new Source {
            Age      = 30,
            Settings = new() { Enabled = true, Level = 7 },
        };

        var settings     = new DestinationSettings { Enabled = false, Level = 2 };
        var destination  = new Destination { Age = 99, Settings = settings };

        mapper.Map(source, destination, new List<string> { "Age" });

        Assert.Equal(30, destination.Age);
        Assert.Same(settings, destination.Settings);
        Assert.False(destination.Settings.Enabled);
        Assert.Equal(2, destination.Settings.Level);
    }

    [Fact]
    public void Map_WithFieldList_Keeps_The_Unmasked_Collection_Instance() {
        var mapper = CreateMapper();

        var source = new Source { Age = 30, Tags = new List<string> { "a", "b" } };

        var tags        = new List<string> { "keep" };
        var destination = new Destination { Age = 99, Tags = tags };

        mapper.Map(source, destination, new List<string> { "Age" });

        Assert.Equal(30, destination.Age);
        Assert.Same(tags, destination.Tags);
        Assert.Equal(new[] { "keep" }, destination.Tags);
    }

    [Fact]
    public void Map_Whole_Field_Selection_Replaces_The_Nested_Object() {
        var mapper = CreateMapper();

        var source = new Source { Address = new() { City = "New", Zip = "111" } };

        var oldAddress  = new DestinationAddress { City = "Old", Zip = "000" };
        var destination = new Destination { Age = 99, Address = oldAddress };

        mapper.Map(source, destination, new List<string> { "Address" });

        Assert.NotSame(oldAddress, destination.Address);
        Assert.Equal("New", destination.Address!.City);
        Assert.Equal("111", destination.Address.Zip);
        Assert.Equal(99, destination.Age);
    }

    [Fact]
    public void Map_Descendant_Selection_Keeps_The_Address_Instance_And_Siblings() {
        var mapper = CreateMapper();

        var source = new Source { Address = new() { City = "New", Zip = "111" } };

        var address     = new DestinationAddress { City = "Old", Zip = "000" };
        var destination = new Destination { Age = 99, Address = address };

        mapper.Map(source, destination, new List<string> { "Address.City" });

        Assert.Same(address, destination.Address);
        Assert.Equal("New", destination.Address!.City);
        Assert.Equal("000", destination.Address.Zip);
        Assert.Equal(99, destination.Age);
    }

    [Fact]
    public void Map_Descendant_Selection_Keeps_The_Subtree_When_The_Projection_Is_Null() {
        var mapper = CreateMapper();

        var source = new Source { Age = 30 };

        var address     = new DestinationAddress { City = "Old", Zip = "000" };
        var destination = new Destination { Address = address };

        mapper.Map(source, destination, new List<string> { "Address.City" });

        Assert.Same(address, destination.Address);
        Assert.Equal("Old", destination.Address!.City);
        Assert.Equal("000", destination.Address.Zip);
    }

    [Fact]
    public void Map_Renamed_Field_Listed_By_Its_Target_Name() {
        var mapper = CreateMapper();

        var source      = new Source { Name = "Alice", Age = 30 };
        var destination = new Destination { Age = 99, Title = "Original", Nickname = "Kept" };

        mapper.Map(source, destination, new List<string> { "Title" });

        Assert.Equal("Alice", destination.Title);
        Assert.Equal(99, destination.Age);
        Assert.Equal("Kept", destination.Nickname);
    }

    [Fact]
    public void Map_Renamed_Field_Keeps_Its_Value_When_Unmasked() {
        var mapper = CreateMapper();

        var source      = new Source { Name = "Alice", Age = 30 };
        var destination = new Destination { Title = "Original", Nickname = "Kept" };

        mapper.Map(source, destination, new List<string> { "Age" });

        Assert.Equal(30, destination.Age);
        Assert.Equal("Original", destination.Title);
        Assert.Equal("Kept", destination.Nickname);
    }
}
