using System.Linq;
using Microsoft.AspNetCore.Http;
using Moq;
using Schemata.Core.Linking;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Flow.Foundation.Linking;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Transport.Http.Linking;
using Schemata.Flow.Grpc.Linking;
using Schemata.Flow.Http.Linking;
using Schemata.Flow.Skeleton;
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
            Process = "processes/process", Spawner = "processes/process/tokens/root",
        };
        var resource = registry.GetResource(typeof(PackagedToken));
        var context = new LinkContext(new ResourceInstanceTarget(typeof(PackagedToken), token.CanonicalName), token, resource);
        var request = new DefaultHttpContext().Request;
        var projector = new HttpLinkProjector([registry], []);
        var standard = new ResourceLinkSemantics(registry, Mock.Of<IResourceLinkPolicy>());
        var links = standard.GetLinks(context).Concat(new FlowLinkContributor<PackagedProcess, PackagedToken, SchemataProcessTransition>().GetLinks(context))
            .Select(link => projector.Project(link, context, request)).OfType<Schemata.Abstractions.Linking.WebLink>().ToList();

        Assert.Equal("/v1/flowv1/processes/process", Assert.Single(links, link => link.Rel == "up").Target);
        Assert.Equal("/v1/flowv1/processes/process/tokens/root",
                     Assert.Single(links, link => link.Rel == "related" && link.Title == "spawner").Target);
    }

    [Fact]
    public void Suppress_Unpublished_Definitions_And_Resource_Operations() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute(typeof(PackagedProcess)) { Operations = [Operations.List] }, []);
        var process = new PackagedProcess { CanonicalName = "processes/process", DefinitionName = "orders", DefinitionVersion = "two" };
        var context = new LinkContext(new ResourceInstanceTarget(typeof(PackagedProcess), process.CanonicalName), process, registry.GetResource(typeof(PackagedProcess)));
        var contributor = new FlowLinkContributor<PackagedProcess, PackagedToken, SchemataProcessTransition>();
        var definitions = Assert.Single(contributor.GetLinks(context));
        var http = new HttpLinkProjector([registry], [new FlowLinkRouteProvider([])]);
        Assert.Null(http.Project(definitions, context, new DefaultHttpContext().Request));
        var grpc = new FlowGrpcLinkTargetResolver([]);
        Assert.Null(grpc.Resolve(definitions.Target));
        Assert.Null(grpc.GetTarget("/schemata.flow.v1.ProcessDefinitionService/ListProcessDefinitions", new Schemata.Flow.Grpc.Wire.ListProcessDefinitionsRequest()));
        Assert.Null(http.Project(new("related", new ResourceInstanceTarget(typeof(PackagedProcess), process.CanonicalName)), context, new DefaultHttpContext().Request));
        Assert.DoesNotContain(contributor.GetLinks(context), link => link.Target is ResourceInstanceTarget { Verb: "catch" });
        Assert.IsType<DomainOperationTarget>(definitions.Target);
        Assert.Equal(FlowConstants.LinkOperations.Definitions, ((DomainOperationTarget)definitions.Target).Operation);
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
