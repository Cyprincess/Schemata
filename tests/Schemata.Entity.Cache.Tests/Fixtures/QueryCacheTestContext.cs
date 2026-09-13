using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Repository;

namespace Schemata.Entity.Cache.Tests.Fixtures;

internal sealed class QueryCacheTestContext : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly ConcurrentDictionary<string, (byte[] Value, TimeSpan? Expires)> _entries = new();
    private readonly IQueryable<Student> _query = Array.Empty<Student>().AsQueryable();
    private readonly Mock<IRepository<Student>> _repository = new();

    internal QueryCacheTestContext() {
        Advice = new(_services);
        UseProviderKey("students");
        Cache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken _) =>
                 _entries.TryGetValue(key, out var entry) && (entry.Expires is null || entry.Expires > Elapsed)
                     ? entry.Value : null);
        Cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                                   It.IsAny<CancellationToken>()))
             .Callback<string, byte[], CacheEntryOptions, CancellationToken>((key, value, options, _) =>
                 Store(key, value, options))
             .Returns(Task.CompletedTask);
        Cache.Setup(x => x.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                                      It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, byte[] value, CacheEntryOptions options, CancellationToken _) =>
                 _entries.TryAdd(key, (value, options.AbsoluteExpirationRelativeToNow is { } ttl ? Elapsed + ttl : null)));
    }

    internal Mock<ICacheProvider> Cache { get; } = new(MockBehavior.Strict);
    internal IRepository<Student> Repository => _repository.Object;
    internal AdviceContext Advice { get; }
    internal SchemataQueryCacheOptions Options { get; } = new();
    internal TimeSpan Elapsed { get; set; }

    internal void UseProviderKey(string key) {
        _repository.As<IQueryCacheKeyProvider>()
            .Setup(p => p.GetQueryCacheKey(It.IsAny<IQueryable<Student>>()))
            .Returns(key);
        _repository.As<IQueryCacheKeyProvider>()
            .Setup(p => p.GetQueryCacheKey(It.IsAny<IQueryable<StudentDto>>()))
            .Returns(key);
    }

    internal void UseUnsupportedProvider() {
        _repository.As<IQueryCacheKeyProvider>()
            .Setup(p => p.GetQueryCacheKey(It.IsAny<IQueryable<Student>>()))
            .Returns((string?)null);
        _repository.As<IQueryCacheKeyProvider>()
            .Setup(p => p.GetQueryCacheKey(It.IsAny<IQueryable<StudentDto>>()))
            .Returns((string?)null);
    }

    internal void Store(string key, byte[] value, CacheEntryOptions options) {
        _entries[key] = (value, options.AbsoluteExpirationRelativeToNow is { } ttl ? Elapsed + ttl : null);
    }

    internal void Remove(string key) { _entries.TryRemove(key, out _); }

    internal QueryContext<Student, Student, T> Query<T>(QueryOperation operation, IRepository<Student>? repository = null) {
        return new(repository ?? Repository, operation, _query);
    }

    internal Task<AdviseResult> Read<TResult, T>(QueryContext<Student, TResult, T> query) {
        return new AdviceQueryCache<Student, TResult, T>(Cache.Object).AdviseAsync(Advice, query);
    }

    internal Task<AdviseResult> Fill<TResult, T>(QueryContext<Student, TResult, T> query, T value) {
        query.Result = value;
        return new AdviceResultCache<Student, TResult, T>(Cache.Object,
            Microsoft.Extensions.Options.Options.Create(Options)).AdviseAsync(Advice, query);
    }

    internal Task<AdviseResult> Commit(CommitChanges<Student> changes) {
        return new AdviceCommittedEvictCache<Student>(Cache.Object,
            Microsoft.Extensions.Options.Options.Create(Options)).AdviseAsync(Advice, Repository, changes);
    }

    public void Dispose() { _services.Dispose(); }
}
