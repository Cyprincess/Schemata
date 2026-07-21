using Schemata.Common;
using Schemata.Resource.Grpc.Internal;
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
}
