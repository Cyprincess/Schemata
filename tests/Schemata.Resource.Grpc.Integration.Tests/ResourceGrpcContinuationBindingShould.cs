using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Client;
using Schemata.Abstractions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

/// <summary>
///     Issue #194 over the gRPC transport: a List continuation binds the issuing tenant, caller
///     identity, and query parameters. Replaying it under another principal, tenant, or parameter
///     set fails with the same invalid-token error as a malformed token, while a same-scope
///     continuation paginates without gaps or duplicates.
/// </summary>
[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceGrpcContinuationBindingShould(WebAppFactory factory)
{
    private static readonly Guid TenantA = Guid.Parse("19419419-4194-4194-8194-194194194190");
    private static readonly Guid TenantB = Guid.Parse("19419419-4194-4194-8194-194194194191");

    [Fact]
    public async Task List_ContinuationReplayedByAnotherPrincipal_ThrowsInvalidArgument() {
        var prefix = $"BindingPrincipal{Guid.NewGuid():n}";
        await SeedLockedStudentsAsync(prefix, 2);

        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<LockedStudent, LockedStudent, LockedStudent, LockedStudent>>(clientFactory);
        var filter = $"full_name:\"{prefix}\"";
        var first  = await client.ListAsync(new() { Filter = filter, PageSize = 1 }, Authenticate("alice"));
        Assert.False(string.IsNullOrEmpty(first.NextPageToken));

        var replayed = await Assert.ThrowsAsync<RpcException>(async () => await client.ListAsync(
            new() { Filter = filter, PageSize = 1, PageToken = first.NextPageToken }, Authenticate("bob")));
        AssertInvalidPageToken(replayed);
    }

    [Fact]
    public async Task List_ContinuationReplayedByAnotherTenant_ThrowsInvalidArgument() {
        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);
        var prefix = $"BindingTenant{Guid.NewGuid():n}";
        await client.CreateAsync(new() { FullName = $"{prefix}-00" });
        await client.CreateAsync(new() { FullName = $"{prefix}-01" });

        var filter = $"full_name:\"{prefix}\"";
        var first  = await client.ListAsync(new() { Filter = filter, PageSize = 1 }, Tenant(TenantA));
        Assert.False(string.IsNullOrEmpty(first.NextPageToken));

        var replayed = await Assert.ThrowsAsync<RpcException>(async () => await client.ListAsync(
            new() { Filter = filter, PageSize = 1, PageToken = first.NextPageToken }, Tenant(TenantB)));
        AssertInvalidPageToken(replayed);
    }

    [Fact]
    public async Task List_ContinuationReplayedWithAlteredParameters_ThrowsInvalidArgument() {
        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);
        var prefix = $"BindingFilter{Guid.NewGuid():n}";
        await client.CreateAsync(new() { FullName = $"{prefix}-00" });
        await client.CreateAsync(new() { FullName = $"{prefix}-01" });

        var first = await client.ListAsync(new() { Filter = $"full_name:\"{prefix}\"", PageSize = 1 });
        Assert.False(string.IsNullOrEmpty(first.NextPageToken));

        var replayed = await Assert.ThrowsAsync<RpcException>(async () => await client.ListAsync(
            new() { Filter = $"full_name:\"{prefix}-altered\"", PageSize = 1, PageToken = first.NextPageToken }));
        AssertInvalidPageToken(replayed);
    }

    [Fact]
    public async Task List_ContinuationWithinSameScope_PaginatesWithoutGapsOrDuplicates() {
        var prefix = $"BindingScope{Guid.NewGuid():n}";
        var seeded = await SeedLockedStudentsAsync(prefix, 3);

        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<LockedStudent, LockedStudent, LockedStudent, LockedStudent>>(clientFactory);
        var visited = new List<string?>();
        string? token = null;
        do {
            var page = await client.ListAsync(new() {
                Filter = $"full_name:\"{prefix}\"", OrderBy = "name asc", PageSize = 1, PageToken = token,
            }, Scope("alice", TenantA));
            visited.AddRange(page.Entities!.Select(row => row.CanonicalName));
            token = page.NextPageToken;
        } while (!string.IsNullOrEmpty(token));

        Assert.Equal(seeded.OrderBy(name => name, StringComparer.Ordinal),
                     visited.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(visited.Count, visited.Distinct().Count());
    }

    private async Task<List<string>> SeedLockedStudentsAsync(string prefix, int count) {
        using var scope = factory.Services.CreateScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>()
                                        .CreateDbContextAsync();
        var names = new List<string>();
        for (var i = 0; i < count; i++) {
            var name = $"lockedStudents/{prefix}-{i:d2}";
            db.LockedStudents.Add(new() { FullName = $"{prefix}-{i:d2}", Name = name, CanonicalName = name });
            names.Add(name);
        }

        await db.SaveChangesAsync();
        return names;
    }

    private static CallContext Authenticate(string subject) =>
        new(new CallOptions(headers: new Metadata { { "X-Test-Auth", "valid" }, { "X-Test-Subject", subject } }));

    private static CallContext Tenant(Guid tenant) =>
        new(new CallOptions(headers: new Metadata { { "X-Test-Tenant", tenant.ToString() } }));

    private static CallContext Scope(string subject, Guid tenant) =>
        new(new CallOptions(headers: new Metadata {
            { "X-Test-Auth", "valid" }, { "X-Test-Subject", subject }, { "X-Test-Tenant", tenant.ToString() },
        }));

    private static void AssertInvalidPageToken(RpcException error) {
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        var status = Google.Rpc.Status.Parser.ParseFrom(error.Trailers.GetValueBytes("grpc-status-details-bin"));
        var badRequest = status.Details
                               .Single(d => d.TypeUrl.EndsWith("google.rpc.BadRequest", StringComparison.Ordinal))
                               .Unpack<BadRequest>();
        var violation = Assert.Single(badRequest.FieldViolations);
        Assert.Equal("page_token", violation.Field);
        Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, violation.Reason);
    }
}
