using Schemata.Core.Building;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Foundation.Handlers;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceIdempotencyPipelineAdvisorShould
{
    private static readonly Guid Timestamp = Guid.Parse("0f8fad5b-d9cb-469f-a666-085b969149fb");

    private static string WeakTag(Guid timestamp) {
        return $"W/\"{
            Convert.ToBase64String(timestamp.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        }\"";
    }

    private static string Hash<T>(T request) {
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
    }

    private static byte[] Done<T>(string hash, T payload) {
        return JsonSerializer.SerializeToUtf8Bytes(new { Kind = "DONE", Hash = hash, Payload = payload });
    }

    private static byte[] Pending(string hash) {
        return JsonSerializer.SerializeToUtf8Bytes(new { Kind = "PENDING", PayloadHash = hash });
    }

    private static Request CreateRequest() {
        return new() { DisplayName = "primary", RequestId = "req-1" };
    }

    private static Detail MappedDetail() {
        return new() {
            CanonicalName = "tenants/t1/hosts/h1",
            Timestamp     = Timestamp,
        };
    }

    [Fact]
    public async Task Create_FinalizedHit_ReplaysCachedDetail_WithoutRunningHandler() {
        var request = CreateRequest();
        var cached  = new Detail { CanonicalName = "tenants/t1/hosts/h9" };
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Done(Hash(request), cached));

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var result = await DispatchCreateAsync(services, request);

        Assert.NotNull(result.Detail);
        Assert.Equal("tenants/t1/hosts/h9", result.Detail.CanonicalName);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
        mapper.Verify(m => m.Map<Request, Entity>(It.IsAny<Request>()), Times.Never);
        cache.Verify(c => c.TryAddAsync(
                         It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                         It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_FinalizedHit_ReplaysCachedDetail_WithoutRunningHandler() {
        var request = CreateRequest();
        var cached  = new Detail { CanonicalName = "tenants/t1/hosts/h9" };
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Done(Hash(request), cached));

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildUpdateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var result = await DispatchUpdateAsync(services, "entities/e1", request);

        Assert.NotNull(result.Detail);
        Assert.Equal("tenants/t1/hosts/h9", result.Detail.CanonicalName);
        repository.Verify(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()), Times.Never);
        owner.Verify(o => o.UpdateAsync(
                         It.IsAny<Entity>(),
                         It.IsAny<IUnitOfWork?>(),
                         It.IsAny<Operations>(),
                         It.IsAny<CancellationToken>()), Times.Never);
        mapper.Verify(m => m.Map<Entity, Detail>(It.IsAny<Entity>()), Times.Never);
        cache.Verify(c => c.TryAddAsync(
                         It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                         It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ConcurrentReservation_WaitsForFinalizedResult_AndReplaysIt() {
        var request = CreateRequest();
        var cache   = new Mock<ICacheProvider>();
        cache.SetupSequence(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Pending(Hash(request)))
             .ReturnsAsync(Pending(Hash(request)))
             .ReturnsAsync(Done(Hash(request), new Detail { CanonicalName = "tenants/t1/hosts/h9" }));
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var result = await DispatchCreateAsync(services, request);

        Assert.NotNull(result.Detail);
        Assert.Equal("tenants/t1/hosts/h9", result.Detail.CanonicalName);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ConcurrentReservation_WithoutFinalizedResult_ThrowsAborted() {
        var request = CreateRequest();
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Pending(Hash(request)));
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false,
            options: new() { IdempotencyPendingWait = TimeSpan.Zero }, owner: owner);

        await Assert.ThrowsAsync<AbortedException>(() => DispatchCreateAsync(services, request));

        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_SameRequestDifferentPayload_FailsValidation() {
        var request = CreateRequest();
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Done("0123456789ABCDEF", new Detail { CanonicalName = "tenants/t1/hosts/h9" }));

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => DispatchCreateAsync(services, request));

        var violation = Assert.Single((exception.Details ?? []).OfType<BadRequestDetail>().SelectMany(d => d.FieldViolations ?? []));
        Assert.Equal("request_id", violation.Field);
        Assert.Equal(ErrorReasons.RequestIdPayloadMismatch, violation.Reason);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ConcurrentDifferentPayload_FailsValidation_WithoutWaitingForTheWinner() {
        var request = CreateRequest();
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Pending("0123456789ABCDEF"));
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false,
            options: new() { IdempotencyPendingWait = TimeSpan.Zero }, owner: owner);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => DispatchCreateAsync(services, request));

        var violation = Assert.Single((exception.Details ?? []).OfType<BadRequestDetail>().SelectMany(d => d.FieldViolations ?? []));
        Assert.Equal("request_id", violation.Field);
        Assert.Equal(ErrorReasons.RequestIdPayloadMismatch, violation.Reason);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_FirstRequest_ReservesPendingRecord_ThenCommitsShapedDetail() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: true);

        var result = await DispatchCreateAsync(services, request);

        var reservation = Assert.Single(reserved);
        Assert.Equal("PENDING", Kind(reservation.Value));
        Assert.Equal("DONE", Kind(store[reservation.Key]));
        Assert.Equal(reservation.Value, Assert.Single(replaced).Expected);
        Assert.Equal(reservation.Key, replaced[0].Key);

        var record = JsonDocument.Parse(reservation.Value).RootElement;
        Assert.Equal(Hash(request), record.GetProperty("PayloadHash").GetString());
        Assert.Equal(string.Empty, record.GetProperty("CanonicalName").GetString());

        // The commit runs behind the detail shaping, so the cached payload carries the
        // Parent and ETag the response family already applied.
        var payload = JsonDocument.Parse(store[reservation.Key]).RootElement.GetProperty("Payload");
        Assert.Equal("tenants/t1", payload.GetProperty("Parent").GetString());
        Assert.Equal(WeakTag(Timestamp), payload.GetProperty("EntityTag").GetString());
        Assert.NotNull(result.Detail);
        Assert.Equal("tenants/t1", result.Detail.Parent);
    }

    [Fact]
    public async Task Update_FirstRequest_ReservesPendingRecord_ThenCommitsShapedDetail() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildUpdateServices(cache, repository, mapper,
            detailWrap: true);

        var result = await DispatchUpdateAsync(services, "entities/e1", request);

        var reservation = Assert.Single(reserved);
        var record = JsonDocument.Parse(reservation.Value).RootElement;
        // Target = envelope.Name: the addressed URI target partitions the key, so the inner
        // request's null name fields cannot alias two different targets onto one key.
        Assert.Equal("entities/e1", record.GetProperty("CanonicalName").GetString());
        Assert.Equal("Update", record.GetProperty("Operation").GetString());

        Assert.Equal("DONE", Kind(store[reservation.Key]));
        var payload = JsonDocument.Parse(store[reservation.Key]).RootElement.GetProperty("Payload");
        Assert.Equal("tenants/t1", payload.GetProperty("Parent").GetString());
        Assert.Equal(WeakTag(Timestamp), payload.GetProperty("EntityTag").GetString());
        Assert.NotNull(result.Detail);
        Assert.Equal("tenants/t1", result.Detail.Parent);
    }

    [Fact]
    public async Task Create_CommitSwapLosesReservation_FallsBackToFreeSlotWrite() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken _) => store.TryGetValue(key, out var value) ? value : null);
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                  if (store.TryAdd(key, value)) {
                      reserved.Add((key, value, options));
                  }
              })
             .ReturnsAsync(true);
        cache.Setup(c => c.TryReplaceAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: true);

        var result = await DispatchCreateAsync(services, request);

        // The expired slot keeps its owner's value; the caller still gets its fresh result.
        // TryReplace fails (swapped=false), so the fallback TryAdd for DONE is also blocked
        // by store.TryAdd returning false (PENDING already occupies the slot). Only PENDING is recorded.
         Assert.Single(reserved);
         Assert.Equal("PENDING", Kind(reserved[0].Value));
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public async Task Create_RequestWithoutRequestId_DispatchesWithoutCacheAccess() {
        var cache = new Mock<ICacheProvider>();

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var result = await DispatchCreateAsync(services, new() { DisplayName = "anonymous" });

        Assert.NotNull(result.Detail);
        Assert.Equal("tenants/t1/hosts/h1", result.Detail.CanonicalName);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(c => c.TryAddAsync(
                         It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                         It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_InstalledWrap_ReservesForIdentifiedRequest() {
        // Suppression is no longer a runtime channel: registration selects the capability, and an
        // installed wrap always consults the cache for identified requests.
        var cache = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        cache.Setup(c => c.TryReplaceAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        var advisor = CreateCreateWrap();
        var ctx     = new AdviceContext(new ServiceCollection().AddSingleton(cache.Object).BuildServiceProvider());
        var envelope = new CreateResourceRequest<Entity, Request, Detail>(CreateRequest(), null);
        var calls   = 0;

        var result = await advisor.AdviseAsync(
            ctx, envelope, _ => {
                calls++;
                // The handler side of the contract: the owned dispatch reports its commit
                // completion on the execution the wrap attached to the envelope.
                envelope.Dispatch!.MarkCommitted();
                return Task.FromResult(new CreateResultBase<Detail> { Detail = MappedDetail() });
            }, CancellationToken.None);

        Assert.Equal(1, calls);
        cache.Verify(c => c.TryAddAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        cache.Verify(c => c.TryReplaceAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_IdempotencyKey_Partitions_On_The_Envelope_Target_Name() {
        // The addressed URI target is the only update key target: the same RequestId against two
        // targets reserves different keys, so A/B targets never cross-reserve.
        var keys  = new List<string>();
        var cache = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .Callback<string, byte[], CacheEntryOptions, CancellationToken>((key, _, _, _) => keys.Add(key))
             .ReturnsAsync(true);
        cache.Setup(c => c.TryReplaceAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildUpdateServices(cache, repository, mapper,
            detailWrap: false);

        await DispatchUpdateAsync(services, "entities/e1", CreateRequest());
        await DispatchUpdateAsync(services, "entities/e2", CreateRequest());

        Assert.Equal(2, keys.Count);
        Assert.NotEqual(keys[0], keys[1]);
    }
    [Fact]
    public async Task Update_RepeatedRequest_ReplaysCommittedResult() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: true, owner: owner);

        var first  = await DispatchCreateAsync(services, request);
        var second = await DispatchCreateAsync(services, request);

        Assert.Single(reserved);
        Assert.Single(replaced);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(first.Detail);
        Assert.NotNull(second.Detail);
        Assert.Equal(first.Detail.CanonicalName, second.Detail.CanonicalName);
        Assert.Equal(first.Detail.Parent, second.Detail.Parent);
        Assert.Equal(first.Detail.EntityTag, second.Detail.EntityTag);
    }

    [Fact]
    public async Task ReserveAndCommit_ApplyConfiguredRetention() {
        var retention = TimeSpan.FromMinutes(90);
        var request   = CreateRequest();
        var store     = new Dictionary<string, byte[]>();
        var reserved  = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced  = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache     = StatefulCache(store, reserved, replaced);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false,
            options: new() { IdempotencyRetention = retention });

        await DispatchCreateAsync(services, request);

        Assert.Equal(retention, Assert.Single(reserved).Options.AbsoluteExpirationRelativeToNow);
        Assert.Equal(retention, Assert.Single(replaced).Options.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public void Advisor_Order_AnchorsAtIdempotency_InsideTheDetailShaping() {
        var advisor = CreateCreateWrap();

        // The dispatcher composes the wrap in ascending Order and unwinds after segments in
        // reverse, so the commit runs behind the detail wrap's Parent/ETag shaping.
        Assert.Equal(SecurityOrders.Idempotency, advisor.Order);
        Assert.True(advisor.Order < ResourceDetailResponsePipelineAdvisor.DefaultOrder);
    }

    [Fact]
    public void AddResource_Registers_ClosedIdempotencyWraps_ForCreateAndUpdate() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddSchemataResources();
        services.AddSingleton(Mock.Of<ICacheProvider>());
        services.AddResource(new ResourceAttribute<Entity, Request, Detail, Summary>(), registry);

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();

        var create = scope.ServiceProvider.GetServices<
            IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>>();
        Assert.Contains(create, a => a is ResourceIdempotencyPipelineAdvisor
            <Entity, Request, CreateResourceRequest<Entity, Request, Detail>, Detail, CreateResultBase<Detail>>);

        var update = scope.ServiceProvider.GetServices<
            IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>>();
        Assert.Contains(update, a => a is ResourceIdempotencyPipelineAdvisor
            <Entity, Request, UpdateResourceRequest<Entity, Request, Detail>, Detail, UpdateResultBase<Detail>>);

        Assert.Contains(services, s =>
            s.ServiceType
         == typeof(IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>)
            && s.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task Create_NoWrite_FreesReservation_SoRetryReRunsHandler() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = new Mock<IResourceMutation<Entity>>();
        owner.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(MutationResult.NoWrite);
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var first = await DispatchCreateAsync(services, request);
        var retry = await DispatchCreateAsync(services, request);

        Assert.Null(first.Detail);
        Assert.Null(retry.Detail);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(2, reserved.Count);
        Assert.Empty(replaced);
        Assert.Empty(store);
    }

    [Fact]
    public async Task Update_NoWrite_FreesReservation_SoRetryReRunsHandler() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = new Mock<IResourceMutation<Entity>>();
        owner.Setup(o => o.UpdateAsync(
                    It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(MutationResult.NoWrite);
        using var services = BuildUpdateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var first = await DispatchUpdateAsync(services, "entities/e1", request);
        var retry = await DispatchUpdateAsync(services, "entities/e1", request);

        Assert.Null(first.Detail);
        Assert.Null(retry.Detail);
        owner.Verify(o => o.UpdateAsync(
                         It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                         It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(2, reserved.Count);
        Assert.Empty(replaced);
        Assert.Empty(store);
    }

    [Fact]
    public async Task Create_PreCommitMappingFailure_RollsBack_AndReleasesReservation_SoRetryReRuns() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var transaction = UnitOfWork();
        var (repository, mapper) = CreateDoubles(MappedDetail(), transaction);
        mapper.Setup(m => m.Map<Entity, Detail>(It.IsAny<Entity>())).Returns((Detail?)null);
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DispatchCreateAsync(services, request));

        // The detail mapping failed before the provider commit, the unit of work rolled back, and
        // the confirmed rollback released the reservation: the key is free again and a retry
        // re-runs the handler instead of waiting out the pending window.
        Assert.Empty(store);
        Assert.Empty(replaced);

        mapper.Setup(m => m.Map<Entity, Detail>(It.IsAny<Entity>())).Returns(MappedDetail());

        var retry = await DispatchCreateAsync(services, request);
        Assert.NotNull(retry.Detail);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Create_FailureAfterTheCommit_KeepsReservationPending_WithoutAnyRemoval() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var transaction = UnitOfWork();
        var (repository, mapper) = CreateDoubles(MappedDetail(), transaction);
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false, owner: owner,
            configure: advisorServices => advisorServices.AddSingleton<
                IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>>(
                new ThrowingAfterResponseAdvisor()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DispatchCreateAsync(services, request));

        // The failure happened after the provider transaction completed: the reservation stays
        // pending as the unfinalized record and is never removed, so a retry inside the retention
        // window cannot repeat the write.
        var reservation = Assert.Single(reserved);
        Assert.Equal("PENDING", Kind(store[reservation.Key]));
        cache.Verify(c => c.TryRemoveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        owner.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ReleasedReservation_ReturnsAWaiterToTheReserveRace_WhereOnlyTheWinnerExecutes() {
        var request = CreateRequest();
        var hash    = Hash(request);
        var cache   = new Mock<ICacheProvider>();

        // Another attempt holds a matching pending reservation, then releases it; the waiter sees
        // the slot vanish and wins the re-opened reserve race.
        cache.SetupSequence(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Pending(hash))
             .ReturnsAsync(Pending(hash))
             .ReturnsAsync((byte[]?)null);
        cache.SetupSequence(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(false)
             .ReturnsAsync(true);
        cache.Setup(c => c.TryReplaceAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = ResourceMutationMock.Create<Entity>();
        using var services = BuildUpdateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var result = await DispatchUpdateAsync(services, "entities/e1", request);

        // The waiter never takes a missing slot for a replay: after the release it re-reserves and,
        // as the winner, executes the dispatch exactly once.
        Assert.NotNull(result.Detail);
        owner.Verify(o => o.UpdateAsync(
                         It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                         It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.TryAddAsync(
                         It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                         It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Create_SettlementRemoval_CannotDeleteAnotherOwnersReservation() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        var owner = new Mock<IResourceMutation<Entity>>();
        owner.Setup(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
             .Callback(() => {
                 // Between this attempt's reserve and its no-write settlement the entry expired and
                 // another attempt reserved the same key with its own bytes.
                 store[reserved[0].Key] = Pending("0123456789ABCDEF");
             })
             .ReturnsAsync(MutationResult.NoWrite);
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false, owner: owner);

        var result = await DispatchCreateAsync(services, request);

        // The release is a compare-and-swap on this attempt's exact reservation bytes: another
        // owner's reservation is left untouched.
        Assert.Null(result.Detail);
        Assert.Equal("PENDING", Kind(store[reserved[0].Key]));
        Assert.Equal("0123456789ABCDEF", JsonDocument.Parse(store[reserved[0].Key]).RootElement.GetProperty("PayloadHash").GetString());
    }

    private sealed class ThrowingAfterResponseAdvisor
        : IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>
    {
        public int Order => ResourceDetailResponsePipelineAdvisor.DefaultOrder + 1;

        public async Task<CreateResultBase<Detail>> AdviseAsync(
            AdviceContext                                                   ctx,
            CreateResourceRequest<Entity, Request, Detail>                  request,
            RequestHandlerContinuation<CreateResultBase<Detail>>            next,
            CancellationToken                                               ct
        ) {
            // Runs inside the mutation: by the time this after segment unwinds, the provider
            // transaction has completed and the wrap's commit evidence is stamped.
            _ = await next(ct);
            throw new InvalidOperationException("response stage failed after the commit");
        }
    }

    #region Helpers

    private static string? Kind(byte[] value) {
        return JsonDocument.Parse(value).RootElement.GetProperty("Kind").GetString();
    }

    private static Mock<ICacheProvider> StatefulCache(
        Dictionary<string, byte[]>                                              store,
        List<(string Key, byte[] Value, CacheEntryOptions Options)>             reserved,
        List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)> replaced
    ) {
        var cache = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken _) => store.TryGetValue(key, out var value) ? value : null);
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                  if (store.TryAdd(key, value)) {
                      reserved.Add((key, value, options));
                  }
              })
             .ReturnsAsync(true);
        cache.Setup(c => c.TryReplaceAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] expected, byte[] replacement, CacheEntryOptions options,
                        CancellationToken _) => replaced.Add((key, expected, replacement, options)))
             .ReturnsAsync((string key, byte[] expected, byte[] replacement, CacheEntryOptions _,
                            CancellationToken _) => {
                  if (!store.TryGetValue(key, out var current) || !current.SequenceEqual(expected)) {
                      return false;
                  }

                  store[key] = replacement;
                  return true;
              });
        cache.Setup(c => c.TryRemoveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, byte[] expected, CancellationToken _) =>
                 store.TryGetValue(key, out var current) && current.SequenceEqual(expected) && store.Remove(key));
        return cache;
    }

    /// <summary>
    ///     A unit-of-work mock honoring the repository contract's settlement timing: commit runs the
    ///     registered commit sinks, disposal without commit runs the rollback sinks. Lets the tests
    ///     observe which completion evidence the handler-owned path produces.
    /// </summary>
    private static Mock<IUnitOfWork> UnitOfWork() {
        var transaction = new Mock<IUnitOfWork>();
        var committed   = new List<Func<CancellationToken, Task>>();
        var rollbacks   = new List<Action>();
        transaction.Setup(t => t.AddCommitSink(It.IsAny<int>(), It.IsAny<Func<CancellationToken, Task>>()))
                   .Callback<int, Func<CancellationToken, Task>>((_, sink) => committed.Add(sink));
        transaction.Setup(t => t.AddRollbackSink(It.IsAny<Action>()))
                   .Callback<Action>(rollbacks.Add);
        transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
                   .Callback(() => {
                       foreach (var sink in committed.ToArray()) {
                           sink(CancellationToken.None).GetAwaiter().GetResult();
                       }
                   })
                   .Returns(Task.CompletedTask);
        transaction.Setup(t => t.DisposeAsync())
                   .Callback(() => {
                       foreach (var reset in rollbacks) {
                           reset();
                       }
                   })
                   .Returns(ValueTask.CompletedTask);
        return transaction;
    }

    private static ResourceIdempotencyPipelineAdvisor<Entity, Request, CreateResourceRequest<Entity, Request, Detail>, Detail, CreateResultBase<Detail>>
        CreateCreateWrap() {
        return new(
            static _ => nameof(Operations.Create),
            static envelope => envelope.Request,
            static envelope => envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
            static detail => new() { Detail = detail },
            static response => response.Detail);
    }
    private static ResourceIdempotencyPipelineAdvisor<Entity, Request, UpdateResourceRequest<Entity, Request, Detail>, Detail, UpdateResultBase<Detail>>
        CreateUpdateWrap() {
        return new(
            static _ => nameof(Operations.Update),
            static envelope => envelope.Request,
            static envelope => envelope.Name,
            static detail => new() { Detail = detail },
            static response => response.Detail);
    }

    private static (Mock<IRepository<Entity>> Repository, Mock<ISimpleMapper> Mapper) CreateDoubles(Detail detail, Mock<IUnitOfWork>? transaction = null) {
        var repository = new Mock<IRepository<Entity>>();
        repository.Setup(r => r.Begin()).Returns(transaction?.Object ?? Mock.Of<IUnitOfWork>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(new ValueTask<Entity?>(new Entity { Name = "e1", CanonicalName = "entities/e1" }));

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>()))
              .Returns(new Entity { Name = "e1", CanonicalName = "entities/e1" });
        mapper.Setup(m => m.Map<Entity, Detail>(It.IsAny<Entity>()))
              .Returns(detail);

        return (repository, mapper);
    }

    private static ServiceProvider BuildCreateServices(
        Mock<ICacheProvider>       cache,
        Mock<IRepository<Entity>>  repository,
        Mock<ISimpleMapper>        mapper,
        bool                       detailWrap,
        SchemataResourceOptions?   options = null,
        Mock<IResourceMutation<Entity>>? owner = null,
        Action<ServiceCollection>? configure = null
    ) {
        owner ??= ResourceMutationMock.Create<Entity>();
        var services = new ServiceCollection();
        services.AddSingleton(cache.Object);
        services.AddSingleton(repository.Object);
        services.AddSingleton(mapper.Object);
        services.AddSingleton(owner.Object);
        services.AddSingleton<ResourceOperationHandler<Entity, Request, Detail, Summary>>();
        services.AddSingleton<
            IRequestHandler<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>,
            DefaultCreateResourceHandler<Entity, Request, Detail, Summary>>();
        if (detailWrap) {
            services.AddSingleton<IEntityTagProvider, DefaultEntityTagProvider>();
            services.AddSingleton<
                IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>,
                ResourceCreateResponsePipelineAdvisor<Entity, Request, Detail>>();
        }
        services.AddSingleton<
            IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>>(
            CreateCreateWrap());
        if (options is not null) {
            services.AddSingleton<IOptions<SchemataResourceOptions>>(Options.Create(options));
        }
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildUpdateServices(
        Mock<ICacheProvider>       cache,
        Mock<IRepository<Entity>>  repository,
        Mock<ISimpleMapper>        mapper,
        bool                       detailWrap,
        SchemataResourceOptions?   options = null,
        Mock<IResourceMutation<Entity>>? owner = null
    ) {
        owner ??= ResourceMutationMock.Create<Entity>();
        var services = new ServiceCollection();
        services.AddSingleton(cache.Object);
        services.AddSingleton(repository.Object);
        services.AddSingleton(mapper.Object);
        services.AddSingleton(owner.Object);
        services.AddSingleton<ResourceOperationHandler<Entity, Request, Detail, Summary>>();
        services.AddSingleton<
            IRequestHandler<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>,
            DefaultUpdateResourceHandler<Entity, Request, Detail, Summary>>();
        if (detailWrap) {
            services.AddSingleton<IEntityTagProvider, DefaultEntityTagProvider>();
            services.AddSingleton<
                IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>,
                ResourceUpdateResponsePipelineAdvisor<Entity, Request, Detail>>();
        }
        services.AddSingleton<
            IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>>(
            CreateUpdateWrap());
        if (options is not null) {
            services.AddSingleton<IOptions<SchemataResourceOptions>>(Options.Create(options));
        }

        return services.BuildServiceProvider();
    }

    private static Task<CreateResultBase<Detail>> DispatchCreateAsync(ServiceProvider services, Request request) {
        var dispatcher = new InProcessRequestDispatcher(services);
        return dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
            new(request, null), CancellationToken.None);
    }

    private static Task<UpdateResultBase<Detail>> DispatchUpdateAsync(ServiceProvider services, string name, Request request) {
        var dispatcher = new InProcessRequestDispatcher(services);
        return dispatcher.SendAsync<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(
            new(name, request, null), CancellationToken.None);
    }

    #endregion

    #region Fixtures

    [CanonicalName("entities/{entity}")]
    public sealed class Entity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Request : ICanonicalName, IRequestIdentification
    {
        public string? DisplayName { get; set; }
        public string? RequestId   { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Detail : ICanonicalName, IChild, IFreshness, IConcurrency
    {
        public string? Parent { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        public string? EntityTag { get; set; }

        public Guid Timestamp { get; set; }
    }

    public sealed class Summary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion
}
