using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Grpc;

public class GrpcResourceNamingShould
{
    [Theory]
    [InlineData(typeof(PackagedStudent), "school.v1.StudentService")]
    [InlineData(typeof(Student), "Schemata.Resource.Tests.Fixtures.StudentService")]
    public void ServiceFullName_UsesResourcePackage_OrNamespaceFallback(Type entityType, string expected) {
        Assert.Equal(expected, GrpcResourceNaming.ServiceFullName(entityType));
    }

    [Theory]
    [InlineData(typeof(Student), "ListStudents")]
    [InlineData(typeof(TrashStudent), "ListTrashStudents")]
    public void MethodName_List_UsesPluralResourceName(Type entityType, string expected) {
        var descriptor = ResourceNameDescriptor.ForType(entityType);

        Assert.Equal(expected, GrpcResourceNaming.MethodName(descriptor, Operations.List));
    }

    [Theory]
    [InlineData(Operations.Get, "GetStudent")]
    [InlineData(Operations.Create, "CreateStudent")]
    [InlineData(Operations.Update, "UpdateStudent")]
    [InlineData(Operations.Delete, "DeleteStudent")]
    public void MethodName_StandardUnary_UsesSingularResourceName(Operations operation, string expected) {
        var descriptor = ResourceNameDescriptor.ForType<PackagedStudent>();

        Assert.Equal(expected, GrpcResourceNaming.MethodName(descriptor, operation));
    }

    [Theory]
    [InlineData("run", "RunStudent")]
    [InlineData("archive", "ArchiveStudent")]
    [InlineData("batchCreate", "BatchCreateStudent")]
    [InlineData("x", "XStudent")]
    [InlineData("", "Student")]
    [InlineData("preview", "PreviewStudent")]
    public void CustomMethodName_PascalCasesVerb_WithSingularResourceName(string verb, string expected) {
        var descriptor = ResourceNameDescriptor.ForType(typeof(Student));

        Assert.Equal(expected, GrpcResourceNaming.CustomMethodName(descriptor, verb));
    }

    #region Nested type: PackagedStudent

    [ResourcePackage("school.v1")]
    [CanonicalName("students/{student}")]
    private sealed class PackagedStudent : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion
}
