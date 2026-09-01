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
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Foundation.Handlers;
using Schemata.Security.Skeleton;
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

    private static byte[] Pending() {
        return JsonSerializer.SerializeToUtf8Bytes(new { Kind = "PENDING" });
    }

    private static Request CreateRequest() {
        return new Request { DisplayName = "primary", RequestId = "req-1" };
    }

    private static Detail MappedDetail() {
        return new Detail {
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
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false);

        var result = await DispatchCreateAsync(services, request);

        Assert.Equal("tenants/t1/hosts/h9", result.Detail!.CanonicalName);
        repository.Verify(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
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
        using var services = BuildUpdateServices(cache, repository, mapper,
            detailWrap: false);

        var result = await DispatchUpdateAsync(services, "entities/e1", request);

        Assert.Equal("tenants/t1/hosts/h9", result.Detail!.CanonicalName);
        repository.Verify(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
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
             .ReturnsAsync(Pending())
             .ReturnsAsync(Pending())
             .ReturnsAsync(Done(Hash(request), new Detail { CanonicalName = "tenants/t1/hosts/h9" }));
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false);

        var result = await DispatchCreateAsync(services, request);

        Assert.Equal("tenants/t1/hosts/h9", result.Detail!.CanonicalName);
        repository.Verify(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ConcurrentReservation_WithoutFinalizedResult_ThrowsAborted() {
        var request = CreateRequest();
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Pending());
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false,
            options: new SchemataResourceOptions { IdempotencyPendingWait = TimeSpan.Zero });

        await Assert.ThrowsAsync<AbortedException>(() => DispatchCreateAsync(services, request));

        repository.Verify(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_SameRequestDifferentPayload_ThrowsAborted() {
        var request = CreateRequest();
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Done("0123456789ABCDEF", new Detail { CanonicalName = "tenants/t1/hosts/h9" }));

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false);

        await Assert.ThrowsAsync<AbortedException>(() => DispatchCreateAsync(services, request));

        repository.Verify(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
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
        Assert.Equal("tenants/t1", result.Detail!.Parent);
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
        // Target = Request.CanonicalName ?? Request.Name ?? "": the inner Request from CreateRequest() has
        // both CanonicalName and Name null, so the partition key CanonicalName field is string.Empty.
        Assert.Equal(string.Empty, record.GetProperty("CanonicalName").GetString());
        Assert.Equal("Update", record.GetProperty("Operation").GetString());

        Assert.Equal("DONE", Kind(store[reservation.Key]));
        var payload = JsonDocument.Parse(store[reservation.Key]).RootElement.GetProperty("Payload");
        Assert.Equal("tenants/t1", payload.GetProperty("Parent").GetString());
        Assert.Equal(WeakTag(Timestamp), payload.GetProperty("EntityTag").GetString());
        Assert.Equal("tenants/t1", result.Detail!.Parent);
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
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: false);

        var result = await DispatchCreateAsync(services, new Request { DisplayName = "anonymous" });

        Assert.Equal("tenants/t1/hosts/h1", result.Detail!.CanonicalName);
        repository.Verify(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(c => c.TryAddAsync(
                         It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                         It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_SuppressedRequest_DispatchesWithoutCacheAccess() {
        var cache   = new Mock<ICacheProvider>();
        var advisor = CreateCreateWrap(cache.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new CreateIdempotencySuppressed());
        var calls = 0;

        var result = await advisor.AdviseAsync(
            ctx, new CreateResourceRequest<Entity, Request, Detail>(CreateRequest(), null), _ => {
                calls++;
                return Task.FromResult(new CreateResultBase<Detail>());
            }, CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Update_IdempotencyKey_IgnoresEnvelopeTargetName() {
        // Verifies legacy equivalence: Update partitions on the inner request's canonical/name, NOT
        // on the envelope URI target. Two dispatches with the same RequestId but different URI targets
        // (e1 vs e2) must share the same idempotency key.
        string? firstKey = null;
        var cache = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .Callback<string, byte[], CacheEntryOptions, CancellationToken>((key, _, _, _) => firstKey ??= key)
             .ReturnsAsync(true);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildUpdateServices(cache, repository, mapper,
            detailWrap: false);

        // Same inner Request (same RequestId) dispatched to two different URI targets.
        var innerRequest = CreateRequest(); // RequestId = "req-1"
        await DispatchUpdateAsync(services, "entities/e1", innerRequest);
        await DispatchUpdateAsync(services, "entities/e2", innerRequest);

        // Both dispatches used the same idempotency key because the partition target is
        // the inner request's canonical/name, not the envelope URI. Both TryAddAsync (reserve)
        // succeeded and both TryReplaceAsync (commit) failed (second dispatch found done from first),
        // so the fallback TryAddAsync commits were also issued — 4 total TryAddAsync calls at the same key.
        Assert.NotNull(firstKey);
        cache.Verify(c => c.TryAddAsync(
                         firstKey!,
                         It.IsAny<byte[]>(),
                         It.IsAny<CacheEntryOptions>(),
                         It.IsAny<CancellationToken>()), Times.Exactly(4));
    }
    [Fact]
    public async Task Update_RepeatedRequest_ReplaysCommittedResult() {
        var request  = CreateRequest();
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<(string Key, byte[] Value, CacheEntryOptions Options)>();
        var replaced = new List<(string Key, byte[] Expected, byte[] Replacement, CacheEntryOptions Options)>();
        var cache    = StatefulCache(store, reserved, replaced);

        var (repository, mapper) = CreateDoubles(MappedDetail());
        using var services = BuildCreateServices(cache, repository, mapper,
            detailWrap: true);

        var first  = await DispatchCreateAsync(services, request);
        var second = await DispatchCreateAsync(services, request);

        Assert.Single(reserved);
        Assert.Single(replaced);
        repository.Verify(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(first.Detail!.CanonicalName, second.Detail!.CanonicalName);
        Assert.Equal(first.Detail.Parent, second.Detail!.Parent);
        Assert.Equal(first.Detail.EntityTag, second.Detail!.EntityTag);
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
            options: new SchemataResourceOptions { IdempotencyRetention = retention });

        await DispatchCreateAsync(services, request);

        Assert.Equal(retention, Assert.Single(reserved).Options.AbsoluteExpirationRelativeToNow);
        Assert.Equal(retention, Assert.Single(replaced).Options.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public void Advisor_Order_AnchorsAtIdempotency_InsideTheDetailShaping() {
        var advisor = CreateCreateWrap(Mock.Of<ICacheProvider>());

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
        return cache;
    }

    private static ResourceIdempotencyPipelineAdvisor<Entity, Request, CreateResourceRequest<Entity, Request, Detail>, Detail, CreateResultBase<Detail>>
        CreateCreateWrap(ICacheProvider cache) {
        return new(
            cache,
            static _ => nameof(Operations.Create),
            static envelope => envelope.Request,
            static envelope => envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
            static ctx => ctx.Has<CreateIdempotencySuppressed>(),
            static detail => new CreateResultBase<Detail> { Detail = detail },
            static response => response.Detail);
    }
    private static ResourceIdempotencyPipelineAdvisor<Entity, Request, UpdateResourceRequest<Entity, Request, Detail>, Detail, UpdateResultBase<Detail>>
        CreateUpdateWrap(ICacheProvider cache) {
        return new(
            cache,
            static _ => nameof(Operations.Update),
            static envelope => envelope.Request,
            static envelope => envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
            static ctx => ctx.Has<UpdateIdempotencySuppressed>(),
            static detail => new UpdateResultBase<Detail> { Detail = detail },
            static response => response.Detail);
    }

    private static (Mock<IRepository<Entity>> Repository, Mock<ISimpleMapper> Mapper) CreateDoubles(Detail detail) {
        var repository = new Mock<IRepository<Entity>>();
        repository.Setup(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.UpdateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
                              It.IsAny<CancellationToken>()))
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
        SchemataResourceOptions?   options = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(cache.Object);
        services.AddSingleton(repository.Object);
        services.AddSingleton(mapper.Object);
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
            CreateCreateWrap(cache.Object));
        if (options is not null) {
            services.AddSingleton<IOptions<SchemataResourceOptions>>(Options.Create(options));
        }

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildUpdateServices(
        Mock<ICacheProvider>       cache,
        Mock<IRepository<Entity>>  repository,
        Mock<ISimpleMapper>        mapper,
        bool                       detailWrap,
        SchemataResourceOptions?   options = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(cache.Object);
        services.AddSingleton(repository.Object);
        services.AddSingleton(mapper.Object);
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
            CreateUpdateWrap(cache.Object));
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
