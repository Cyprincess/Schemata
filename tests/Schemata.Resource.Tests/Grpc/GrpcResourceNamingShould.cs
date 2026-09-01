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
    [InlineData("run", "RunStudent")]
    [InlineData("archive", "ArchiveStudent")]
    [InlineData("batchCreate", "BatchCreateStudent")]
    [InlineData("x", "XStudent")]
    [InlineData("", "Student")]
    public void Concat_PascalCasedVerbWithSingular(string verb, string expected) {
        var descriptor = ResourceNameDescriptor.ForType(typeof(Student));

        Assert.Equal(expected, GrpcResourceNaming.CustomMethodName(descriptor, verb));
    }

    [Fact]
    public void ServiceFullName_UsesResourcePackageWhenPresent() {
        var name = GrpcResourceNaming.ServiceFullName(typeof(PackagedStudent));

        Assert.Equal("school.v1.StudentService", name);
    }

    [Fact]
    public void MethodName_List_UsesPluralResourceName() {
        var descriptor = ResourceNameDescriptor.ForType<PackagedStudent>();

        var name = GrpcResourceNaming.MethodName(descriptor, Operations.List);

        Assert.Equal("ListStudents", name);
    }

    [Fact]
    public void MethodName_StandardUnary_UsesSingularResourceName() {
        var descriptor = ResourceNameDescriptor.ForType<PackagedStudent>();

        var name = GrpcResourceNaming.MethodName(descriptor, Operations.Delete);

        Assert.Equal("DeleteStudent", name);
    }

    [Fact]
    public void CustomMethodName_UsesVerbAndSingularResourceName() {
        var descriptor = ResourceNameDescriptor.ForType<PackagedStudent>();

        var name = GrpcResourceNaming.CustomMethodName(descriptor, "preview");

        Assert.Equal("PreviewStudent", name);
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
