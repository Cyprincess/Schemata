using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class TraitParserShould
{
    [Fact]
    public void Parse_BasicTrait() {
        var input  = "Trait Identifier {\n  Note 'Primary Key'\n  long id [primary key]\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Identifier", result.Name);
        Assert.Single(result.Fields);
        Assert.Single(result.Notes);
        Assert.Equal("long", result.Fields[0].Type);
        Assert.Equal("id", result.Fields[0].Name);
        Assert.Contains(FieldOption.PrimaryKey, result.Fields[0].Options);
    }

    [Fact]
    public void Parse_TraitWithBases() {
        var input  = "Trait Entity : Identifier, Timestamp {\n  long id\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(2, result.Bases.Length);
        Assert.Equal("Identifier", result.Bases[0]);
        Assert.Equal("Timestamp", result.Bases[1]);
    }

    [Fact]
    public void Parse_TraitWithUses() {
        var input  = "Trait Entity {\n  Use Identifier, Timestamp\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Uses);
        Assert.Equal(2, result.Uses[0].QualifiedNames.Length);
        Assert.Contains("Identifier", result.Uses[0].QualifiedNames);
        Assert.Contains("Timestamp", result.Uses[0].QualifiedNames);
    }

    [Fact]
    public void Parse_CaseInsensitive() {
        var input  = "trait Foo {\n  string bar\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Foo", result.Name);
    }

    [Fact]
    public void Parse_TraitWithMultipleFields() {
        var input  = "Trait Timestamp {\n  timestamp? creation_date\n  timestamp? modification_date\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(2, result.Fields.Length);
        Assert.True(result.Fields[0].Nullable);
        Assert.True(result.Fields[1].Nullable);
    }

    [Fact]
    public void Parse_TraitWithNoteAndUseAndField() {
        var input  = "Trait Auditable {\n  Note 'Audit trail'\n  Use Timestamp\n  string modified_by\n}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Notes);
        Assert.Single(result.Uses);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void Parse_TraitEmptyBody() {
        var input  = "Trait Marker {}";
        var result = Parser.Trait.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Marker", result.Name);
        Assert.Equal(0, result.Fields.Length);
    }
}
