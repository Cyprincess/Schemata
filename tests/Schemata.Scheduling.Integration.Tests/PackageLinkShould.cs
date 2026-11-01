using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Scheduling.Http.Linking;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests;

[Trait("Category", "Unit")]
public class PackageLinkShould
{
    [Fact]
    public void Execution_Collection_Uses_The_Execution_Package() {
        var registry = Registry();
        var job      = new PackagedJob { Name = "job", CanonicalName = "jobs/job" };
        var context  = Context(job, registry.GetResource(typeof(PackagedJob))!);

        var link = Assert.Single(new SchedulingLinkContributor(registry).GetLinks(context));

        Assert.Equal("related", link.Rel);
        Assert.StartsWith("/v1/sched/operations?filter=", link.Target);
    }

    [Fact]
    public void Job_Back_Link_Uses_The_Job_Package() {
        var registry  = Registry();
        var operation = new Operation { Metadata = new() { Job = "jobs/job" } };
        var context   = Context(operation, registry.GetResource(typeof(PackagedExecution))!);

        var link = Assert.Single(new SchedulingLinkContributor(registry).GetLinks(context));

        Assert.Equal("/v1/sched/jobs/job", link.Target);
    }

    private static ResourceRegistry Registry() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute(typeof(PackagedJob)), []);
        registry.Add(new ResourceAttribute(typeof(PackagedExecution)), []);

        return registry;
    }

    private static LinkContext Context(object value, ResourceRegistration resource) {
        return new(new DefaultHttpContext(), new JsonResult(value), resource);
    }

    [CanonicalName("jobs/{job}")]
    [ResourcePackage("sched")]
    private sealed class PackagedJob : SchemataJob;

    [CanonicalName("operations/{operation}")]
    [ResourcePackage("sched")]
    private sealed class PackagedExecution : SchemataJobExecution;
}
