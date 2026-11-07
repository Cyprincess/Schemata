using System;
using System.Linq;
using System.Threading.Tasks;
using Google.Rpc;
using Grpc.Core;
using ProtoBuf.Grpc.Client;
using Schemata.Abstractions;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceGrpcErrorIntegrationShould
{
    private readonly WebAppFactory _factory;

    public ResourceGrpcErrorIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Get_NonExistentName_ThrowsNotFound() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var rpc = await Assert.ThrowsAsync<RpcException>(() => client
                                                              .GetAsync(new() {
                                                                   CanonicalName = "students/does-not-exist-99999",
                                                               })
                                                              .AsTask());

        Assert.Equal(StatusCode.NotFound, rpc.StatusCode);
    }

    [Fact]
    public async Task List_IncompatibleLiteral_ThrowsInvalidFilter() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            client.ListAsync(new() { Filter = "age = 'hello'" }).AsTask());

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        var status = Google.Rpc.Status.Parser.ParseFrom(error.Trailers.GetValueBytes("grpc-status-details-bin"));
        var badRequest = status.Details
                               .Single(d => d.TypeUrl.EndsWith("google.rpc.BadRequest", StringComparison.Ordinal))
                               .Unpack<BadRequest>();
        var violation = Assert.Single(badRequest.FieldViolations);
        Assert.Equal("filter", violation.Field);
        Assert.Equal(SchemataResources.INVALID_FILTER, violation.Reason);
    }

    [Fact]
    public async Task Update_WrongETag_ThrowsAborted() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var created = await client.CreateAsync(new() { FullName = "ETagCanary" });
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.CanonicalName), "CanonicalName should be auto-set");

        var update = new Student {
            FullName = "Updated", EntityTag = "W/\"wrong-etag-value\"", CanonicalName = created.CanonicalName,
        };

        var rpc = await Assert.ThrowsAsync<RpcException>(() => client.UpdateAsync(update).AsTask());

        Assert.Equal(StatusCode.Aborted, rpc.StatusCode);
    }
}
