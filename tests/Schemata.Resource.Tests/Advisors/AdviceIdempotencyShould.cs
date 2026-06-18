using System;
using System.Collections.Generic;
using System.Security.Claims;
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
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceIdempotencyShould
{
    [Fact]
    public async Task Create_NoRequestId_Continues() {
        var cache     = new Mock<ICacheProvider>();
        var advisor   = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request   = new StudentRequest();
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        cache.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NewRequestId_StoresPendingKeyAndContinues() {
        var cache = new Mock<ICacheProvider>();
        cache.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
        cache.Setup(s => s.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                                       It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);

        var advisor   = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request   = new StudentRequest { RequestId = "req-123" };
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        cache.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(
            s => s.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                               It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateRequestId_ReturnsCachedResultAndHandles() {
        var cache = new FakeCache();

        var first = await RunCreateAsync(cache, new() { RequestId = "req-123", FullName = "Alice" }, null);
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "Cached" });
        var reservations = cache.TryAddCount;

        var replay = await RunCreateAsync(cache, new() { RequestId = "req-123", FullName = "Alice" }, null);

        Assert.Equal(AdviseResult.Handle, replay.Result);
        Assert.Equal("Cached", replay.Ctx.Get<CreateResultBase<Student>>()?.Detail?.FullName);
        Assert.Equal(reservations, cache.TryAddCount);
    }

    [Fact]
    public async Task Create_ConcurrentRequestId_ThrowsConcurrencyException() {
        // No completed result yet, but the atomic reservation loses to a concurrent request.
        var cache = new Mock<ICacheProvider>();
        cache.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
        cache.Setup(s => s.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                                       It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<SchemataResourceOptions>>(
            Options.Create(new SchemataResourceOptions { IdempotencyPendingWait = TimeSpan.Zero }));

        var advisor   = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache.Object);
        var ctx       = new AdviceContext(services.BuildServiceProvider());
        var request   = new StudentRequest { RequestId = "req-concurrent" };
        var container = new ResourceRequestContainer<Student>();

        await Assert.ThrowsAsync<ConcurrencyException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }

    [Fact]
    public async Task Create_SuppressIdempotency_Continues() {
        var cache   = new Mock<ICacheProvider>();
        var advisor = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new CreateIdempotencySuppressed());
        var request   = new StudentRequest { RequestId = "req-456" };
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        cache.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ReplayWithSamePayload_ReturnsCachedResult() {
        var cache = new FakeCache();

        var first = await RunCreateAsync(cache, new() { RequestId = "req-1", FullName = "Alice" }, null);
        Assert.Equal(AdviseResult.Continue, first.Result);
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "Created" });

        var replay = await RunCreateAsync(cache, new() { RequestId = "req-1", FullName = "Alice" }, null);

        Assert.Equal(AdviseResult.Handle, replay.Result);
        Assert.Equal("Created", replay.Ctx.Get<CreateResultBase<Student>>()?.Detail?.FullName);
    }

    [Fact]
    public async Task Create_ReplayWithDifferentPayload_ThrowsConcurrencyException() {
        var cache = new FakeCache();

        var first = await RunCreateAsync(cache, new() { RequestId = "req-1", FullName = "Alice" }, null);
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "Created" });

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => RunCreateAsync(cache, new() { RequestId = "req-1", FullName = "Mallory" }, null));
    }

    [Fact]
    public async Task Create_SameRequestId_DifferentPrincipal_DoesNotCollide() {
        var cache = new FakeCache();

        var first = await RunCreateAsync(cache, new() { RequestId = "req-1", FullName = "Alice" }, Principal("user-1"));
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "Created" });

        var other = await RunCreateAsync(cache, new() { RequestId = "req-1", FullName = "Alice" }, Principal("user-2"));

        Assert.Equal(AdviseResult.Continue, other.Result);
        Assert.False(other.Ctx.TryGet<CreateResultBase<Student>>(out _));
    }

    [Fact]
    public async Task Create_SameRequestId_DifferentEntityType_DoesNotCollide() {
        var cache = new FakeCache();

        var first = await RunCreateAsync(cache, new() { RequestId = "req-1", FullName = "Alice" }, null);
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "Created" });

        var advisor   = new AdviceCreateRequestIdempotency<Teacher, StudentRequest, Teacher>(cache);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Teacher>();

        var result = await advisor.AdviseAsync(ctx, new() { RequestId = "req-1", FullName = "Alice" }, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.False(ctx.TryGet<CreateResultBase<Teacher>>(out _));
    }

    [Fact]
    public async Task SameRequestId_DifferentResource_NoCollision() {
        var cache = new FakeCache();

        var first = await RunUpdateAsync(cache,
                                         new() { RequestId = "req-shared", CanonicalName = "students/a", FullName = "Alice" },
                                         null);
        Assert.Equal(AdviseResult.Continue, first.Result);
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "ResourceA" });

        var other = await RunUpdateAsync(cache,
                                         new() { RequestId = "req-shared", CanonicalName = "students/b", FullName = "Bob" },
                                         null);

        Assert.Equal(AdviseResult.Continue, other.Result);
        Assert.False(other.Ctx.TryGet<UpdateResultBase<Student>>(out _));
    }

    [Fact]
    public async Task Update_ReplayWithSamePayload_ReturnsCachedResult() {
        var cache = new FakeCache();

        var first = await RunUpdateAsync(cache, new() { RequestId = "req-update", FullName = "Alice" }, null);
        Assert.Equal(AdviseResult.Continue, first.Result);
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "Updated" });

        var replay = await RunUpdateAsync(cache, new() { RequestId = "req-update", FullName = "Alice" }, null);

        Assert.Equal(AdviseResult.Handle, replay.Result);
        Assert.Equal("Updated", replay.Ctx.Get<UpdateResultBase<Student>>()?.Detail?.FullName);
    }

    [Fact]
    public async Task Method_ReplayWithSamePayload_ReturnsCachedResponse() {
        var cache = new FakeCache();

        var first = await RunMethodAsync(cache, new() { RequestId = "req-method", FullName = "Alice" }, null);
        Assert.Equal(AdviseResult.Continue, first.Result);
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "Method" });

        var replay = await RunMethodAsync(cache, new() { RequestId = "req-method", FullName = "Alice" }, null);

        Assert.Equal(AdviseResult.Handle, replay.Result);
        Assert.Equal("Method", replay.Ctx.Get<Student>()?.FullName);
    }

    [Fact]
    public async Task Method_PurgeSameRequestId_ReturnsCachedOperation() {
        var cache = new FakeCache();

        var first = await RunPurgeMethodAsync(cache, new() { RequestId = "req-purge", Filter = "*" });
        Assert.Equal(AdviseResult.Continue, first.Result);
        await PersistOperationAsync(cache, first.Ctx, new() { Name = "op-1", CanonicalName = "operations/op-1" });

        var replay = await RunPurgeMethodAsync(cache, new() { RequestId = "req-purge", Filter = "*" });

        Assert.Equal(AdviseResult.Handle, replay.Result);
        Assert.Equal("operations/op-1", replay.Ctx.Get<Operation>()?.CanonicalName);
    }

    [Fact]
    public async Task Response_StoresOperationNeutralEnvelope() {
        var cache = new FakeCache();

        var first = await RunUpdateAsync(cache, new() { RequestId = "req-neutral", FullName = "Alice" }, null);
        await PersistResponseAsync(cache, first.Ctx, new() { FullName = "Updated" });

        var bytes = await cache.GetRawAsync();
        using var document = JsonDocument.Parse(bytes);
        Assert.True(document.RootElement.TryGetProperty("Payload", out var payload));
        Assert.Equal("Updated", payload.GetProperty(nameof(Student.FullName)).GetString());
        Assert.False(document.RootElement.TryGetProperty("Result", out _));
    }

    private static async Task<(AdviseResult Result, AdviceContext Ctx)> RunCreateAsync(
        ICacheProvider          cache,
        StudentRequest          request,
        ClaimsPrincipal? principal
    ) {
        var advisor   = new AdviceCreateRequestIdempotency<Student, StudentRequest, Student>(cache);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, principal);
        return (result, ctx);
    }

    private static async Task<(AdviseResult Result, AdviceContext Ctx)> RunUpdateAsync(
        ICacheProvider                          cache,
        StudentRequest                          request,
        ClaimsPrincipal? principal
    ) {
        var advisor   = new AdviceUpdateRequestIdempotency<Student, StudentRequest, Student>(cache);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, principal);
        return (result, ctx);
    }

    private static async Task<(AdviseResult Result, AdviceContext Ctx)> RunMethodAsync(
        ICacheProvider                          cache,
        StudentRequest                          request,
        ClaimsPrincipal? principal
    ) {
        var advisor   = new AdviceMethodRequestIdempotency<Student, StudentRequest, Student>(cache);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        ctx.Set(new ResourceMethodVerb("customVerb"));

        var result = await advisor.AdviseAsync(ctx, request, container, principal);
        return (result, ctx);
    }

    private static async Task<(AdviseResult Result, AdviceContext Ctx)> RunPurgeMethodAsync(
        ICacheProvider cache,
        PurgeRequest   request
    ) {
        var advisor   = new AdviceMethodRequestIdempotency<Student, PurgeRequest, Operation>(cache);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        ctx.Set(new ResourceMethodVerb("purge"));

        var result = await advisor.AdviseAsync(ctx, request, container, null);
        return (result, ctx);
    }

    private static Task PersistResponseAsync(ICacheProvider cache, AdviceContext ctx, Student detail) {
        var advisor = new AdviceResponseIdempotency<Student, Student>(cache);
        return advisor.AdviseAsync(ctx, new(), detail, null);
    }

    private static Task PersistOperationAsync(ICacheProvider cache, AdviceContext ctx, Operation operation) {
        var advisor = new AdviceResponseIdempotency<Student, Operation>(cache);
        return advisor.AdviseAsync(ctx, new(), operation, null);
    }

    private static ClaimsPrincipal Principal(string subject) {
        var identity = new ClaimsIdentity([new("sub", subject)], "test");
        return new(identity);
    }

    private sealed class Teacher : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    private sealed class FakeCache : ICacheProvider
    {
        private readonly Dictionary<string, byte[]> _store = [];

        public int TryAddCount { get; private set; }

        public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
            return Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);
        }

        public Task SetAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> TryAddAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
            TryAddCount++;
            return Task.FromResult(_store.TryAdd(key, value));
        }

        public Task<byte[]> GetRawAsync() {
            return Task.FromResult(Assert.Single(_store.Values));
        }

        public Task<bool> TryReplaceAsync(string key, byte[] expected, byte[] replacement, CacheEntryOptions options, CancellationToken ct = default) {
            if (_store.TryGetValue(key, out var current) && current.AsSpan().SequenceEqual(expected)) {
                _store[key] = replacement;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
            if (_store.TryGetValue(key, out var current) && current.AsSpan().SequenceEqual(expected)) {
                _store.Remove(key);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task CollectionAddAsync(string key, string member, CacheEntryOptions options, CancellationToken ct = default) {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
            throw new NotSupportedException();
        }

        public Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
            throw new NotSupportedException();
        }

        public Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
            throw new NotSupportedException();
        }

        public Task CollectionClearAsync(string key, CancellationToken ct = default) {
            throw new NotSupportedException();
        }
    }
}
