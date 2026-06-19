using System.ComponentModel;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Grpc.Internal;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

public class GrpcResourceNamingShould
{
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
    [DisplayName("Student")]
    private sealed class PackagedStudent : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion
}
