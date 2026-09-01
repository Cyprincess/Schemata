using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class OperationTransportNamingShould
{
    [Fact]
    public void ExposeTheExecutionRowAsTheOperationResource() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataJobExecution>();

        Assert.Equal("Operation", descriptor.Singular);
        Assert.Equal("Operations", descriptor.Plural);
        Assert.Equal("operations", descriptor.Collection);
        Assert.Equal("operations", descriptor.CollectionPath);
    }

    [Fact]
    public void NameTheGrpcServiceAndListRpcAfterTheResource() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataJobExecution>();

        Assert.Equal("OperationService", GrpcResourceNaming.ServiceName(descriptor));
        Assert.Equal("ListOperations", GrpcResourceNaming.MethodName(descriptor, Operations.List));
        Assert.Equal("GetOperation", GrpcResourceNaming.MethodName(descriptor, Operations.Get));
        Assert.Equal("WaitOperation", GrpcResourceNaming.CustomMethodName(descriptor, "wait"));
    }

    [Fact]
    public void KeepTheJobResourceNamesDistinctFromTheOperationResource() {
        var descriptor = ResourceNameDescriptor.ForType<SchemataJob>();

        Assert.Equal("Job", descriptor.Singular);
        Assert.Equal("Jobs", descriptor.Plural);
        Assert.Equal("jobs", descriptor.CollectionPath);
        Assert.Equal("JobService", GrpcResourceNaming.ServiceName(descriptor));
        Assert.Equal("ListJobs", GrpcResourceNaming.MethodName(descriptor, Operations.List));
    }
}
