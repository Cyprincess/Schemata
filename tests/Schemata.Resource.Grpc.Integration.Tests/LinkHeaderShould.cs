using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using ProtoBuf.Grpc.Client;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Schemata.Transport.Grpc.Linking;
using Schemata.Transport.Http.Linking;
using Schemata.Core.Building;
using Schemata.Core.Linking;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Layer", "Integration")]
public class LinkHeaderShould(WebAppFactory factory)
{
    [Fact]
    public void Nested_Target_Projects_Parent_Filter_And_Continuation_On_Both_Transports() {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Note>(), []);
        var target = new ResourceCollectionTarget(typeof(Note), "students/one", "text=hello",
            new System.Collections.Generic.Dictionary<string, string?> { ["page_token"] = "protected" });
        var context = new LinkContext(target, null, registry.GetResource(typeof(Note)));
        var semantic = new SemanticLink("next", target);
        var http = new HttpLinkProjector([registry], []).Project(semantic, context, new Microsoft.AspNetCore.Http.DefaultHttpContext().Request);
        var grpc = new GrpcLinkProjector([new ResourceGrpcLinkTargetResolver(registry)]).Project(semantic, context);
        Assert.NotNull(http);
        Assert.NotNull(grpc);
        Assert.Equal(http.Rel, grpc.Rel);
        Assert.StartsWith("/v1/students/one/notes?", http.Target);
        Assert.Contains("page_token=protected", http.Target);
        Assert.Equal($"/{GrpcResourceNaming.ServiceFullName(typeof(Note))}/ListNotes", grpc.Target);
        Assert.Equal("students/one", grpc.Extensions!["parent"]);
        Assert.Equal("text=hello", grpc.Extensions["filter"]);
        Assert.Equal("protected", grpc.Extensions["page_token"]);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("excluded")]
    [InlineData("custom")]
    public void Rpc_Target_Requires_Enabled_Registered_Operation(string reason) {
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<Note> {
            Endpoints = reason == "disabled" ? [HttpResourceAttribute.Name] : [GrpcResourceAttribute.Name],
            Operations = reason == "excluded" ? [Operations.List] : [Operations.Get],
        }, []);
        var target = new ResourceInstanceTarget(typeof(Note), "students/one/notes/two", reason == "custom" ? "catch" : null);
        Assert.Null(new ResourceGrpcLinkTargetResolver(registry).Resolve(target));
    }

    [CanonicalName("students/{student}/notes/{note}")]
    private sealed class Note;

    [Fact]
    public async Task Read_And_List_Project_FullRpc_And_Typed_Inputs() {
        var (channel, clients) = factory.CreateGrpcChannelWithClient();
        using (channel) {
            var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clients);
            var label = "Link" + Guid.NewGuid().ToString("n");
            var created = await client.CreateAsync(new() { FullName = label });
            _ = await client.CreateAsync(new() { FullName = label });
            Assert.NotNull(created);
            var descriptor = ResourceNameDescriptor.ForType<Student>();
            var service = GrpcResourceNaming.ServiceFullName(typeof(Student));
            var model = RuntimeTypeModel.Create();
            model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
            foreach (var type in new[] { typeof(GetRequest), typeof(ListRequest), typeof(Student), typeof(ListResultBase<Student, Student>) })
                SchemataProtoModelConfigurator.ConfigureType(model, type);
            var read = new Method<GetRequest, Student>(MethodType.Unary, service,
                GrpcResourceNaming.MethodName(descriptor, Operations.Get), GrpcMarshallers.Create<GetRequest>(model), GrpcMarshallers.Create<Student>(model));
            var invoker = channel.CreateCallInvoker();
            using var get = invoker.AsyncUnaryCall(read, null, new(), new GetRequest { CanonicalName = created.CanonicalName });
            Assert.Equal(created.CanonicalName, (await get.ResponseAsync).CanonicalName);
            var readLinks = LinkHeader.Parse(get.GetTrailers().Single(entry => entry.Key == "link").Value);
            Assert.Equal(new[] { "self", "canonical" }, readLinks.Select(link => link.Rel));
            Assert.All(readLinks, link => {
                Assert.Equal(read.FullName, link.Target);
                Assert.Equal(created.CanonicalName, link.Extensions!["name"]);
            });
            var list = new Method<ListRequest, ListResultBase<Student, Student>>(MethodType.Unary, service,
                GrpcResourceNaming.MethodName(descriptor, Operations.List), GrpcMarshallers.Create<ListRequest>(model),
                GrpcMarshallers.Create<ListResultBase<Student, Student>>(model));
            var filter = $"full_name=\"{label}\"";
            using var first = invoker.AsyncUnaryCall(list, null, new(), new ListRequest { Filter = filter, PageSize = 1 });
            var page = await first.ResponseAsync;
            Assert.Single(page.Entities!);
            var next = Assert.Single(LinkHeader.Parse(first.GetTrailers().Single(entry => entry.Key == "link").Value), link => link.Rel == "next");
            Assert.Equal(list.FullName, next.Target);
            Assert.Equal(page.NextPageToken, next.Extensions!["page_token"]);
            Assert.Equal(filter, next.Extensions["filter"]);
            using var second = invoker.AsyncUnaryCall(list, null, new(), new ListRequest { Filter = filter, PageToken = next.Extensions["page_token"] });
            Assert.Single((await second.ResponseAsync).Entities!);
            var firstLink = Assert.Single(LinkHeader.Parse(second.GetTrailers().Single(entry => entry.Key == "link").Value), link => link.Rel == "first");
            using var replay = invoker.AsyncUnaryCall(list, null, new(), new ListRequest { Filter = filter, PageToken = firstLink.Extensions!["page_token"] });
            Assert.Equal(page.Entities![0].CanonicalName, (await replay.ResponseAsync).Entities![0].CanonicalName);
        }
    }
}
