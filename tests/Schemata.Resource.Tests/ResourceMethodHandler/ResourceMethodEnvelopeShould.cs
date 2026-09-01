using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Foundation.Handlers;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class ResourceMethodEnvelopeShould
{
    private static readonly Guid Timestamp = Guid.Parse("0f8fad5b-d9cb-469f-a666-085b969149fb");

    private static string WeakTag(Guid timestamp) {
        return $"W/\"{
            Convert.ToBase64String(timestamp.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        }\"";
    }

    private static string Hash(MethodRequest request) {
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
    }

    private static byte[] Done(string hash, MethodResponse payload) {
        return JsonSerializer.SerializeToUtf8Bytes(new { Kind = "DONE", Hash = hash, Payload = payload });
    }

    [Fact]
    public async Task Envelope_Dispatch_Reaches_The_Method_Handler_Once() {
        var handler    = new MethodHandler();
        var request    = new MethodRequest();
        var principal  = new ClaimsPrincipal(new ClaimsIdentity());
        using var services = BuildServices(handler: handler);

        var response = await DispatchAsync(services, "archive", null, request, principal);

        Assert.Same(handler.Response, response);
        Assert.Equal(1, handler.Invocations);
        Assert.Same(request, handler.Request);
        Assert.Same(principal, handler.Request!.Principal);
    }

    [Fact]
    public async Task Wrap_Advisor_Reads_Verb_Name_And_Entity_From_The_Envelope() {
        var wrap       = new RecordingEnvelopeAdvisor();
        var handler    = new MethodHandler();
        using var services = BuildServices(repository: LoadedRepository().Object, handler: handler, configure: services => {
            services.AddSingleton<IRequestPipelineAdvisor<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>>(wrap);
        });

        await DispatchAsync(services, "archive", "entities/e1", new MethodRequest { CanonicalName = "entities/payload" }, null);

        var observed = Assert.Single(wrap.Observed);
        Assert.Equal("archive", observed.Verb);
        Assert.Equal("entities/e1", observed.Name);
        Assert.Equal(typeof(MethodEntity), observed.Entity);
        Assert.Equal(1, handler.Invocations);
    }

    [Fact]
    public async Task Instance_Name_Routes_The_Envelope_To_Entity_Loading() {
        var entity  = new MethodEntity { Name = "e1", CanonicalName = "entities/e1" };
        var handler = new MethodHandler();
        var entityAdvisor = new RecordingMethodEntityAdvisor();
        var repository = new Mock<IRepository<MethodEntity>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                       It.IsAny<Func<IQueryable<MethodEntity>, IQueryable<MethodEntity>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<MethodEntity?>(entity));
        using var services = BuildServices(repository: repository.Object, handler: handler, configure: services => {
            services.AddSingleton<IResourceMethodAdvisor<MethodEntity, MethodRequest, MethodResponse>>(entityAdvisor);
        });

        var response = await DispatchAsync(services, "archive", "entities/e1", new MethodRequest(), null);

        Assert.Same(handler.Response, response);
        Assert.Same(entity, entityAdvisor.Entity);
        Assert.Equal(1, handler.Invocations);
    }

    [Fact]
    public async Task Method_Response_Wrap_Derives_Parent_And_Sets_Weak_ETag() {
        var handler = new MethodHandler {
            Response = new MethodResponse { CanonicalName = "tenants/t1/hosts/h1", Timestamp = Timestamp },
        };
        using var services = BuildServices(handler: handler, configure: services => {
            services.AddSingleton<IEntityTagProvider, DefaultEntityTagProvider>();
            services.AddSingleton<
                IRequestPipelineAdvisor<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>,
                ResourceMethodResponsePipelineAdvisor<MethodEntity, MethodRequest, MethodResponse>>();
        });

        var response = await DispatchAsync(services, "archive", null, new MethodRequest(), null);

        Assert.Equal("tenants/t1", response.Parent);
        Assert.Equal(WeakTag(Timestamp), response.EntityTag);
    }

    [Fact]
    public async Task Method_Idempotency_Replays_A_Finalized_Response_Without_Running_The_Handler() {
        var request = new MethodRequest { RequestId = "req-1" };
        var cached  = new MethodResponse { CanonicalName = "tenants/t1/hosts/h9" };
        var handler = new MethodHandler();
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Done(Hash(request), cached));
        using var services = BuildServices(handler: handler, configure: services => {
            services.AddSingleton(cache.Object);
            services.AddSingleton<
                IRequestPipelineAdvisor<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>,
                ResourceMethodIdempotencyPipelineAdvisor<MethodEntity, MethodRequest, MethodResponse>>();
        });

        var response = await DispatchAsync(services, "archive", "entities/e1", request, null);

        Assert.Equal("tenants/t1/hosts/h9", response.CanonicalName);
        Assert.Equal(0, handler.Invocations);
        cache.Verify(c => c.TryAddAsync(
                         It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                         It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Method_Idempotency_Reserves_With_Verb_And_Target_Then_Commits_The_Produced_Response() {
        var request = new MethodRequest { RequestId = "req-1" };
        var handler = new MethodHandler {
            Response = new MethodResponse { CanonicalName = "tenants/t1/hosts/h1", Timestamp = Timestamp },
        };
        var store    = new Dictionary<string, byte[]>();
        var reserved = new List<byte[]>();
        var replaced = new List<byte[]>();
        // The wrap hashes the wire payload before the pipeline stamps the route target onto it.
        var expectedHash = Hash(request);
        var cache    = StatefulCache(store, reserved, replaced);
        using var services = BuildServices(repository: LoadedRepository().Object, handler: handler, configure: services => {
            services.AddSingleton(cache.Object);
            services.AddSingleton<
                IRequestPipelineAdvisor<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>,
                ResourceMethodIdempotencyPipelineAdvisor<MethodEntity, MethodRequest, MethodResponse>>();
        });

        var response = await DispatchAsync(services, "archive", "entities/e1", request, null);

        Assert.Same(handler.Response, response);

        var pending = Assert.Single(reserved);
        var record  = JsonDocument.Parse(pending).RootElement;
        Assert.Equal("archive", record.GetProperty("Operation").GetString());
        Assert.Equal("entities/e1", record.GetProperty("CanonicalName").GetString());

        var done = Assert.Single(replaced);
        var envelope = JsonDocument.Parse(done).RootElement;
        Assert.Equal("DONE", envelope.GetProperty("Kind").GetString());
        Assert.Equal(expectedHash, envelope.GetProperty("Hash").GetString());
        Assert.Equal("tenants/t1/hosts/h1", envelope.GetProperty("Payload").GetProperty("CanonicalName").GetString());
    }

    [Fact]
    public async Task Delete_Dispatch_Derives_Parent_And_Sets_Weak_ETag() {
        var detail = new SoftDetail { CanonicalName = "tenants/t1/hosts/h1", Timestamp = Timestamp };
        var repository = new Mock<IRepository<SoftEntity>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SoftEntity>, IQueryable<SoftEntity>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<SoftEntity?>(new SoftEntity { Name = "e1", CanonicalName = "entities/e1" }));
        repository.Setup(r => r.RemoveAsync(It.IsAny<SoftEntity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var mapper = new Mock<Schemata.Mapping.Skeleton.ISimpleMapper>();
        mapper.Setup(m => m.Map<SoftEntity, SoftDetail>(It.IsAny<SoftEntity>())).Returns(detail);
        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton(mapper.Object);
        services.AddSingleton<IResourceDeleteAdvisor<SoftEntity>>(new SoftDeleteStampingAdvisor());
        services.AddSingleton<ResourceOperationHandler<SoftEntity, SoftRequest, SoftDetail, SoftSummary>>();
        services.AddSingleton<
            IRequestHandler<DeleteResourceRequest<SoftEntity, SoftDetail>, DeleteResultBase<SoftDetail>>,
            DefaultDeleteResourceHandler<SoftEntity, SoftRequest, SoftDetail, SoftSummary>>();
        services.AddSingleton<IEntityTagProvider, DefaultEntityTagProvider>();
        services.AddSingleton<
            IRequestPipelineAdvisor<DeleteResourceRequest<SoftEntity, SoftDetail>, DeleteResultBase<SoftDetail>>,
            ResourceDeleteResponsePipelineAdvisor<SoftEntity, SoftDetail>>();
        using var provider = services.BuildServiceProvider();

        var result = await new InProcessRequestDispatcher(provider).SendAsync
            <DeleteResourceRequest<SoftEntity, SoftDetail>, DeleteResultBase<SoftDetail>>(
                new("entities/e1", null, null), CancellationToken.None);

        Assert.Equal("tenants/t1", result.Detail!.Parent);
        Assert.Equal(WeakTag(Timestamp), result.Detail.EntityTag);
    }

    [Fact]
    public void AddResource_Registers_The_Envelope_Handler_And_The_Method_Wraps() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddSchemataResources();
        services.AddSingleton(Mock.Of<ICacheProvider>());
        services.AddResource(new ResourceAttribute<MethodEntity, MethodRequest, MethodResponse, MethodResponse> {
            Methods = [new("archive", typeof(MethodHandler))],
        }, registry);

        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestHandler<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>)
            && service.ImplementationType == typeof(ResourceMethodDispatchHandler<MethodEntity, MethodRequest, MethodResponse>));
        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestPipelineAdvisor<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>)
            && service.ImplementationType == typeof(ResourceMethodResponsePipelineAdvisor<MethodEntity, MethodRequest, MethodResponse>)
            && service.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestPipelineAdvisor<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>)
            && service.ImplementationType == typeof(ResourceMethodIdempotencyPipelineAdvisor<MethodEntity, MethodRequest, MethodResponse>)
            && service.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void Method_Wraps_Order_The_Shaping_Above_The_Idempotency_Commit() {
        Assert.Equal(SecurityOrders.Idempotency,
            new ResourceMethodIdempotencyPipelineAdvisor<MethodEntity, MethodRequest, MethodResponse>(Mock.Of<ICacheProvider>()).Order);
        Assert.Equal(ResourceDetailResponsePipelineAdvisor.DefaultOrder,
            new ResourceMethodResponsePipelineAdvisor<MethodEntity, MethodRequest, MethodResponse>(Mock.Of<IEntityTagProvider>()).Order);
        Assert.True(ResourceDetailResponsePipelineAdvisor.DefaultOrder > SecurityOrders.Idempotency);
    }

    private static Task<MethodResponse> DispatchAsync(
        ServiceProvider    services,
        string             verb,
        string?            name,
        MethodRequest      request,
        ClaimsPrincipal?   principal
    ) {
        var dispatcher = new InProcessRequestDispatcher(services);
        return dispatcher.SendAsync<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>(
            new(verb, name, request, principal), CancellationToken.None);
    }

    private static ServiceProvider BuildServices(
        IRepository<MethodEntity>?     repository = null,
        MethodHandler?                 handler    = null,
        Action<ServiceCollection>?     configure  = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(repository ?? Mock.Of<IRepository<MethodEntity>>());
        services.AddSingleton<IRequestHandler<MethodRequest, MethodResponse>>(handler ?? new MethodHandler());
        services.AddSingleton(sp => new ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>(
            sp.GetRequiredService<IRepository<MethodEntity>>(), sp, new InProcessRequestDispatcher(sp)));
        services.AddSingleton<
            IRequestHandler<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>,
            ResourceMethodDispatchHandler<MethodEntity, MethodRequest, MethodResponse>>();
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static Mock<IRepository<MethodEntity>> LoadedRepository() {
        var repository = new Mock<IRepository<MethodEntity>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                       It.IsAny<Func<IQueryable<MethodEntity>, IQueryable<MethodEntity>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<MethodEntity?>(new MethodEntity { Name = "e1", CanonicalName = "entities/e1" }));
        return repository;
    }

    private static Mock<ICacheProvider> StatefulCache(
        Dictionary<string, byte[]>                              store,
        List<byte[]>                                            reserved,
        List<byte[]>                                            replaced
    ) {
        var cache = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken _) => store.TryGetValue(key, out var value) ? value : null);
        cache.Setup(c => c.TryAddAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                  reserved.Add(value);
                  store.TryAdd(key, value);
              })
             .ReturnsAsync(true);
        cache.Setup(c => c.TryReplaceAsync(
                  It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                  It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] expected, byte[] replacement, CacheEntryOptions options,
                        CancellationToken _) => replaced.Add(replacement))
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

    private sealed class RecordingEnvelopeAdvisor : IRequestPipelineAdvisor<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>
    {
        public List<(string Verb, string? Name, Type Entity)> Observed { get; } = [];

        public int Order => 0;

        public Task<MethodResponse> AdviseAsync(
            AdviceContext                                                          ctx,
            ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>    request,
            RequestHandlerContinuation<MethodResponse>                             next,
            CancellationToken                                                      ct = default
        ) {
            Observed.Add((request.Verb, request.Name, request.GetType().GetGenericArguments()[0]));
            return next(ct);
        }
    }

    private sealed class RecordingMethodEntityAdvisor : IResourceMethodAdvisor<MethodEntity, MethodRequest, MethodResponse>
    {
        public MethodEntity? Entity { get; private set; }

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext     ctx,
            MethodRequest     request,
            MethodEntity      entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct = default
        ) {
            Entity = entity;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class SoftDeleteStampingAdvisor : IResourceDeleteAdvisor<SoftEntity>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext     ctx,
            DeleteRequest     request,
            SoftEntity        entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct = default
        ) {
            entity.DeleteTime = DateTime.UtcNow;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    [CanonicalName("entities/{entity}")]
    public sealed class MethodEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class MethodRequest : ICanonicalName, ICommand<MethodResponse>, IRequestPrincipal, IRequestIdentification
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public string? RequestId     { get; set; }
        public ClaimsPrincipal? Principal { get; set; }
    }

    public sealed class MethodResponse : ICanonicalName, IChild, IFreshness, IConcurrency
    {
        public string? Parent { get; set; }
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public string? EntityTag { get; set; }
        public Guid Timestamp { get; set; }
    }

    public sealed class MethodHandler : IRequestHandler<MethodRequest, MethodResponse>
    {
        public int Invocations { get; private set; }

        public MethodRequest? Request { get; private set; }

        public MethodResponse Response { get; set; } = new() { CanonicalName = "entities/handled" };

        public Task<MethodResponse> HandleAsync(MethodRequest request, CancellationToken ct = default) {
            Invocations++;
            Request = request;
            return Task.FromResult(Response);
        }
    }

    [CanonicalName("entities/{entity}")]
    public sealed class SoftEntity : ICanonicalName, ISoftDelete
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime { get; set; }
    }

    public sealed class SoftRequest : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class SoftDetail : ICanonicalName, IChild, IFreshness, IConcurrency
    {
        public string? Parent { get; set; }
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public string? EntityTag { get; set; }
        public Guid Timestamp { get; set; }
    }

    public sealed class SoftSummary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
