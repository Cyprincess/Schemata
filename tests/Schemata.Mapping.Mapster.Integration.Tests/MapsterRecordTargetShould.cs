using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Mapping.Mapster.Integration.Tests.Fixtures;
using Schemata.Mapping.Skeleton;
using Xunit;

namespace Schemata.Mapping.Mapster.Integration.Tests;

[Trait("Category", "Integration")]
public class MapsterRecordTargetShould
{
    private static ISimpleMapper CreateMapper() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
            schema.UseMapster()
                  .Map<RecordSource, RecordDestination>()
                  .Map<SourceProfile, DestinationProfile>();
        });

        var app   = builder.Build();
        var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISimpleMapper>();
    }

    private static RecordDestination Existing() {
        return new("Original", 0.1m, 5, true, ["keep"], "note");
    }

    [Fact]
    public void MaskedRecordUpdate_PopulatedDecimal_PersistsAndKeepsUnmasked() {
        var mapper      = CreateMapper();
        var source      = new RecordSource("ignored", 0.2m, 9, false, ["new"], "changed");
        var destination = Existing();

        mapper.Map(source, destination, ["Rate"]);

        Assert.Equal(0.2m, destination.Rate);
        Assert.Equal("Original", destination.Description);
        Assert.Equal(5, destination.Count);
        Assert.True(destination.Active);
        Assert.Equal(new List<string> { "keep" }, destination.Tags);
        Assert.Equal("note", destination.Note);
    }

    [Fact]
    public void MaskedRecordUpdate_PopulatedString_Persists() {
        var mapper      = CreateMapper();
        var source      = new RecordSource("Updated", 0.2m, 9, false, ["new"], "changed");
        var destination = Existing();

        mapper.Map(source, destination, ["Description"]);

        Assert.Equal("Updated", destination.Description);
        Assert.Equal(0.1m, destination.Rate);
        Assert.Equal("note", destination.Note);
    }

    [Fact]
    public void MaskedRecordUpdate_PopulatedCollection_Persists() {
        var mapper      = CreateMapper();
        var source      = new RecordSource("ignored", 0.2m, 9, false, ["a", "b"], "changed");
        var destination = Existing();

        mapper.Map(source, destination, ["Tags"]);

        Assert.Equal(new List<string> { "a", "b" }, destination.Tags);
        Assert.Equal(0.1m, destination.Rate);
        Assert.Equal("Original", destination.Description);
    }

    [Fact]
    public void MaskedRecordUpdate_ExplicitFalse_ClearsMaskedField() {
        var mapper      = CreateMapper();
        var source      = new RecordSource("ignored", 0.2m, 9, false, ["new"], "changed");
        var destination = Existing();

        mapper.Map(source, destination, ["Active"]);

        Assert.False(destination.Active);
        Assert.Equal(0.1m, destination.Rate);
    }

    [Fact]
    public void MaskedRecordUpdate_ExplicitZero_ClearsMaskedField() {
        var mapper      = CreateMapper();
        var source      = new RecordSource("ignored", 0.2m, 0, false, ["new"], "changed");
        var destination = Existing();

        mapper.Map(source, destination, ["Count"]);

        Assert.Equal(0, destination.Count);
        Assert.Equal(0.1m, destination.Rate);
        Assert.True(destination.Active);
    }

    [Fact]
    public void MaskedRecordUpdate_ExplicitNull_ClearsMaskedField() {
        var mapper      = CreateMapper();
        var source      = new RecordSource(null, 0.2m, 9, false, ["new"], "changed");
        var destination = Existing();

        mapper.Map(source, destination, ["Description"]);

        Assert.Null(destination.Description);
        Assert.Equal(0.1m, destination.Rate);
        Assert.Equal("note", destination.Note);
    }

    [Fact]
    public void MaskedClassUpdate_PopulatedString_WritesOnlyTheMaskedMember() {
        var mapper      = CreateMapper();
        var source      = new SourceProfile { DisplayName = "Updated", Bio = "new", Locale = "fr" };
        var destination = new DestinationProfile { DisplayName = "Original", Bio = "old", Locale = "en" };

        mapper.Map(source, destination, ["DisplayName"]);

        Assert.Equal("Updated", destination.DisplayName);
        Assert.Equal("old", destination.Bio);
        Assert.Equal("en", destination.Locale);
    }
}
