using System;
using System.Linq;
using Schemata.Abstractions.Entities;
using Xunit;

namespace Schemata.Entity.Repository.Tests;

public class SchemataPrimaryKeyShould
{
    [Fact]
    public void ResolveKeyProperties_CompositeAttribute_PreservesDeclarationOrder() {
        var properties = RepositoryBase.ResolveKeyProperties(typeof(CompositeEntity));

        Assert.Equal([nameof(CompositeEntity.Partition), nameof(CompositeEntity.Sequence)], properties.Select(p => p.Name));
    }

    [Fact]
    public void ResolveKeyProperties_WithoutAttribute_FallsBackToIdentifierUid() {
        var properties = RepositoryBase.ResolveKeyProperties(typeof(IdentifierEntity));

        Assert.Equal([nameof(IdentifierEntity.Uid)], properties.Select(p => p.Name));
    }

    [PrimaryKey(nameof(Partition), nameof(Sequence))]
    private sealed class CompositeEntity
    {
        public string Partition { get; set; } = string.Empty;

        public int Sequence { get; set; }
    }

    private sealed class IdentifierEntity : IIdentifier
    {
        public Guid Uid { get; set; }
    }
}
