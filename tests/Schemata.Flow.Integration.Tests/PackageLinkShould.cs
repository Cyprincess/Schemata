using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Flow.Http.Linking;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Unit")]
public class PackageLinkShould
{
    [Fact]
    public void Token_Process_And_Spawner_Targets_Use_The_Flow_Package() {
        var registry = Registry();
        var token = new PackagedToken {
            Name = "token", CanonicalName = "processes/process/tokens/token",
            Process = "process", Spawner = "processes/process/tokens/root",
        };
        var context = new LinkContext(new DefaultHttpContext(), new JsonResult(token), null);

        var links = new FlowLinkContributor(registry).GetLinks(context).ToList();

        Assert.Equal("/v1/flowv1/processes/process", Assert.Single(links, link => link.Rel == "up").Target);
        Assert.Equal("/v1/flowv1/processes/process/tokens/root",
                     Assert.Single(links, link => link.Rel == "related" && link.Title == "spawner").Target);
    }

    private static ResourceRegistry Registry() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute(typeof(PackagedProcess)), []);
        registry.Add(new ResourceAttribute(typeof(PackagedToken)), []);

        return registry;
    }

    [CanonicalName("processes/{process}")]
    [ResourcePackage("flowv1")]
    private sealed class PackagedProcess : SchemataProcess;

    [CanonicalName("processes/{process}/tokens/{token}")]
    [ResourcePackage("flowv1")]
    private sealed class PackagedToken : SchemataProcessToken;
}
