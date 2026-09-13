using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Tests;

public sealed class DefinitionsListWireNameShould
{
    [Fact]
    public void Name_The_Definitions_List_Field_After_The_Definition_Resource() {
        var wire = ResourceWireNameRules.ResolveWireName(
            typeof(ListResultBase<ProcessDefinitionInfo, ProcessDefinitionInfo>), "Entities");

        Assert.Equal("Definitions", wire);
    }
}
