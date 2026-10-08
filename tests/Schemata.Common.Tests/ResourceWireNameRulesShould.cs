using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Xunit;

namespace Schemata.Common.Tests;

public class ResourceWireNameRulesShould
{
    [Fact]
    public void Resolve_List_Entities_From_The_Entity_When_The_Summary_Lacks_A_Pattern() {
        var wire = ResourceWireNameRules.ResolveWireName(typeof(ListResultBase<StudentEntity, NamelessSummary>), "Entities");

        Assert.Equal("Students", wire);
    }

    [Fact]
    public void Ignore_A_Summary_Pattern_When_Naming_List_Entities() {
        var wire = ResourceWireNameRules.ResolveWireName(typeof(ListResultBase<StudentEntity, MisleadingSummary>), "Entities");

        Assert.Equal("Students", wire);
    }

    [Fact]
    public void Name_List_Entities_Independently_When_Two_Entities_Share_A_Summary() {
        var students = ResourceWireNameRules.ResolveWireName(typeof(ListResultBase<StudentEntity, NamelessSummary>), "Entities");
        var people   = ResourceWireNameRules.ResolveWireName(typeof(ListResultBase<PersonEntity, NamelessSummary>), "Entities");

        Assert.Equal("Students", students);
        Assert.Equal("People", people);
    }

    [Fact]
    public void Keep_The_Entity_Collection_As_Authored_For_Irregular_Plurals() {
        var wire = ResourceWireNameRules.ResolveWireName(typeof(ListResultBase<PersonEntity, NamelessSummary>), "Entities");

        Assert.Equal("People", wire);
    }

    [Fact]
    public void Fall_Back_To_The_Type_Name_Plural_When_The_Entity_Lacks_A_Pattern() {
        var wire = ResourceWireNameRules.ResolveWireName(typeof(ListResultBase<WidgetRecord, WidgetRecord>), "Entities");

        Assert.Equal("WidgetRecords", wire);
    }

    [Fact]
    public void Fall_Back_To_Type_Name_Labels_Without_Granting_Addressability() {
        var descriptor = ResourceNameDescriptor.ForType<WidgetRecord>();

        Assert.Null(descriptor.Pattern);
        Assert.False(descriptor.IsAddressable);
        Assert.Equal(string.Empty, descriptor.Collection);
        Assert.Equal("WidgetRecord", descriptor.Singular);
        Assert.Equal("WidgetRecords", descriptor.Plural);
    }

    [Fact]
    public void Resolve_List_Entities_Back_From_The_Entity_Plural() {
        var property = ResourceWireNameRules.ResolveClrName(typeof(ListResultBase<PersonEntity, NamelessSummary>), "People");

        Assert.Equal(nameof(IEntitiesResult<,>.Entities), property);
    }

    [CanonicalName("students/{student}")]
    private sealed class StudentEntity : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("people/{person}")]
    private sealed class PersonEntity : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    private sealed class NamelessSummary : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("bogus/{bogus}")]
    private sealed class MisleadingSummary : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    private sealed class WidgetRecord : ICanonicalName
    {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }
}
