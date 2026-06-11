using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Mapping.AutoMapper.Integration.Tests.Fixtures;
using Schemata.Mapping.Skeleton;
using Xunit;

namespace Schemata.Mapping.AutoMapper.Integration.Tests;

[Trait("Category", "Integration")]
public class AutoMapperMergeShould
{
    private static ISimpleMapper CreateMapper() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
            schema.UseAutoMapper()
                  .Map<Source, Destination>(map => {
                       map.For(d => d.Sex).From(s => s.Sex.ToString());
                   });
        });

        var app   = builder.Build();
        var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISimpleMapper>();
    }

    [Fact]
    public void Merge_NullSourceMember_PreservesDestinationValue() {
        var mapper = CreateMapper();

        var source      = new Source { Age      = 31, Nickname = null };
        var destination = new Destination { Age = 1, Nickname = "Kept" };

        mapper.Map(source, destination);

        Assert.Equal(31, destination.Age);
        Assert.Equal("Kept", destination.Nickname);
    }

    [Fact]
    public void Merge_BlankSourceMember_PreservesDestinationValue() {
        var mapper = CreateMapper();

        var source      = new Source { Age      = 31, Nickname = "   " };
        var destination = new Destination { Age = 1, Nickname = "Kept" };

        mapper.Map(source, destination);

        Assert.Equal("Kept", destination.Nickname);
    }

    [Fact]
    public void Merge_PopulatedSourceMember_Overwrites() {
        var mapper = CreateMapper();

        var source      = new Source { Nickname      = "New" };
        var destination = new Destination { Nickname = "Old" };

        mapper.Map(source, destination);

        Assert.Equal("New", destination.Nickname);
    }

    [Fact]
    public void Merge_FieldList_StillClearsMaskedFields() {
        var mapper = CreateMapper();

        var source      = new Source { Nickname      = null };
        var destination = new Destination { Nickname = "Old" };

        mapper.Map(source, destination, ["Nickname"]);

        Assert.Null(destination.Nickname);
    }

    [Fact]
    public void Map_NewInstance_CopiesNulls() {
        var mapper = CreateMapper();

        var source = new Source { Nickname = null };

        var result = mapper.Map<Source, Destination>(source);

        Assert.NotNull(result);
        Assert.Null(result.Nickname);
    }
}
