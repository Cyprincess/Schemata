using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public class LinkHeaderShould : IClassFixture<WebAppFactory>, IClassFixture<GrpcWebAppFactory>, IClassFixture<ParticipantWebAppFactory>
{
    private readonly WebAppFactory             _http;
    private readonly GrpcWebAppFactory         _grpc;
    private readonly ParticipantWebAppFactory  _participant;

    public LinkHeaderShould(WebAppFactory http, GrpcWebAppFactory grpc, ParticipantWebAppFactory participant) {
        _http        = http;
        _grpc        = grpc;
        _participant = participant;
    }

    [Fact]
    public async Task Process_Token_And_Transition_Expose_Relation_Links() {
        var (process, token, transition) = await SeedAsync(_http.Services);

        var processResponse = await _http.CreateClient().GetAsync($"/v1/processes/{process}");
        Assert.Equal(HttpStatusCode.OK, processResponse.StatusCode);
        var processLinks = Links(processResponse);
        Assert.Single(processLinks, link => link.Rel == "self");
        Assert.Single(processLinks, link => link.Rel == "canonical");
        Assert.Contains(processLinks, link => link.Rel == "related" && link.Title == "Tokens" && link.Target.EndsWith($"/v1/processes/{process}/tokens"));
        Assert.Contains(processLinks, link => link.Rel == "related" && link.Title == "Transitions" && link.Target.EndsWith($"/v1/processes/{process}/transitions"));
        Assert.Contains(processLinks, link => link.Rel == "related" && link.Target.EndsWith("/v1/processes:definitions"));
        Assert.DoesNotContain(processLinks, link => link.Target.Contains("catch", StringComparison.OrdinalIgnoreCase));

        var tokenResponse = await _http.CreateClient().GetAsync($"/v1/processes/{process}/tokens/{token}");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var tokenLinks = Links(tokenResponse);
        var parent     = Assert.Single(tokenLinks, link => link.Rel == "up");
        Assert.EndsWith($"/v1/processes/{process}", parent.Target);

        var transitionResponse = await _http.CreateClient().GetAsync($"/v1/processes/{process}/transitions/{transition}");
        Assert.Equal(HttpStatusCode.OK, transitionResponse.StatusCode);
        var transitionLinks = Links(transitionResponse);
        Assert.EndsWith($"/v1/processes/{process}", Assert.Single(transitionLinks, link => link.Rel == "up").Target);
        Assert.EndsWith($"/v1/processes/{process}/tokens/{token}",
                        Assert.Single(transitionLinks, link => link.Rel == "related" && link.Title == "token").Target);
    }

    [Fact]
    public async Task Definitions_List_Emits_Self() {
        var response = await _http.CreateClient().GetAsync("/v1/processes:definitions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var self = Assert.Single(Links(response), link => link.Rel == "self");
        Assert.EndsWith("/v1/processes:definitions", self.Target);
    }

    [Fact]
    public async Task Unauthenticated_And_Unauthorized_Requests_Have_No_Link_Header() {
        var unauthenticated = await _participant.CreateClient().GetAsync("/v1/processes");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.False(unauthenticated.Headers.Contains(LinkHeader.Name));

        var (process, _, _) = await SeedAsync(_participant.Services);
        var client          = _participant.CreateClient();
        client.DefaultRequestHeaders.Add("X-Subject", "users/without-grant");
        var forbidden = await client.GetAsync($"/v1/processes/{process}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.False(forbidden.Headers.Contains(LinkHeader.Name));
    }

    [Fact]
    public async Task Grpc_Get_Carries_No_Link_Header() {
        var (process, _, _) = await SeedAsync(_grpc.Services);

        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(SchemataProcess));
        var descriptor = ResourceNameDescriptor.ForType<SchemataProcess>();
        var method = new Method<GetRequest, SchemataProcess>(
            MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(SchemataProcess)),
            GrpcResourceNaming.MethodName(descriptor, Operations.Get),
            GrpcMarshallers.Create<GetRequest>(model),
            GrpcMarshallers.Create<SchemataProcess>(model));

        using var call = _grpc.CreateGrpcChannel().CreateCallInvoker()
                               .AsyncUnaryCall(method, null, new(), new GetRequest { CanonicalName = $"processes/{process}" });
        Assert.Equal($"processes/{process}", (await call.ResponseAsync).CanonicalName);

        var headers = await call.ResponseHeadersAsync;
        Assert.DoesNotContain(headers, entry => string.Equals(entry.Key, LinkHeader.Name, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(call.GetTrailers(),
                              entry => string.Equals(entry.Key, LinkHeader.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(string Process, string Token, string Transition)> SeedAsync(IServiceProvider services) {
        var suffix    = Guid.NewGuid().ToString("n");
        var process   = $"link-{suffix}";
        var token     = $"token-{suffix}";
        var transition = $"transition-{suffix}";

        using var scope    = services.CreateScope();
        var       database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        database.Processes.Add(new SchemataProcess {
            Uid = Guid.NewGuid(), Name = process, CanonicalName = $"processes/{process}",
            DefinitionName = "link-definition", DefinitionVersion = "one", State = "Running",
        });
        database.ProcessTokens.Add(new SchemataProcessToken {
            Uid = Guid.NewGuid(), Name = token, CanonicalName = $"processes/{process}/tokens/{token}",
            Process = process, ScopeName = string.Empty, StateName = "task", WaitingAtName = "catch",
        });
        database.ProcessTransitions.Add(new SchemataProcessTransition {
            Uid = Guid.NewGuid(), Name = transition, CanonicalName = $"processes/{process}/transitions/{transition}",
            Process = process, Token = $"processes/{process}/tokens/{token}", Event = "advance", Kind = TransitionKind.Move,
        });
        await database.SaveChangesAsync();

        return (process, token, transition);
    }

    private static IReadOnlyList<WebLink> Links(HttpResponseMessage response) {
        Assert.True(response.Headers.TryGetValues(LinkHeader.Name, out var values), "The response carries no Link header.");
        return LinkHeader.Parse(string.Join(", ", values), response.RequestMessage!.RequestUri);
    }
}
