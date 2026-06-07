using Schemata.Resource.Grpc;
using Xunit;

namespace Schemata.Resource.Tests.Grpc;

public class ResourceMethodNamingShould
{
    [Theory]
    [InlineData("run",          "Job",      "RunJob")]
    [InlineData("archive",      "Book",     "ArchiveBook")]
    [InlineData("batchCreate",  "Book",     "BatchCreateBook")]
    [InlineData("signDocument", "Document", "SignDocumentDocument")]
    public void ConcatPascalCasedVerbWithSingular(string verb, string singular, string expected) {
        Assert.Equal(expected, ResourceMethodNaming.GetRpcName(verb, singular));
    }

    [Fact]
    public void PreserveSingleCharVerb_AsUpperCase() {
        Assert.Equal("XJob", ResourceMethodNaming.GetRpcName("x", "Job"));
    }

    [Fact]
    public void ReturnSingularUnchanged_WhenVerbIsEmpty() {
        Assert.Equal("Job", ResourceMethodNaming.GetRpcName(string.Empty, "Job"));
    }
}
