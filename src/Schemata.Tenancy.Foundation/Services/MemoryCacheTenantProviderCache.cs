using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Tenancy.Skeleton;

namespace Schemata.Tenancy.Foundation.Services;

/// <summary>
///     LRU <see cref="ITenantProviderCache" /> with capacity-bound synchronous eviction
///     and sliding expiration. Disposal of retired providers is deferred until every
///     outstanding lease has been released so active tenant scopes keep their provider.
/// </summary>
public sealed class MemoryCacheTenantProviderCache : ITenantProviderCache, IDisposable, IAsyncDisposable
{
    private readonly int                                       _capacity;
    private readonly object                                    _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index;
    private readonly LinkedList<Entry>                         _order = [];
    private readonly Dictionary<string, Pending>               _pending = [];
    private readonly TimeProvider                              _time;
    private readonly TimeSpan                                  _ttl;
    private          bool                                      _disposed;

    [ThreadStatic]
    private static HashSet<Pending>? _constructing;

    /// <summary>
    ///     Initializes a new instance with capacity and sliding expiration sourced from <see cref="SchemataTenancyOptions" />.
    /// </summary>
    /// <param name="options">Tenancy options supplying capacity and sliding expiration.</param>
    /// <param name="time">Clock used for sliding-expiration eviction; defaults to the system clock.</param>
    public MemoryCacheTenantProviderCache(IOptions<SchemataTenancyOptions> options, TimeProvider? time = null) {
        _ttl      = options.Value.ProviderSlidingExpiration;
        _capacity = options.Value.ProviderMaxCapacity;
        _index    = new(_capacity);
        _time     = time ?? TimeProvider.System;
    }

    #region IDisposable Members

    public void Dispose() {
        var orphaned = RetireAll();
        DisposeProviders(orphaned);
    }

    public async ValueTask DisposeAsync() {
        var orphaned = RetireAll();
        List<Exception>? failures = null;
        foreach (var entry in orphaned) {
            try { await DisposeProviderAsync(entry.Provider); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    #endregion

    #region ITenantProviderCache Members

    public ITenantProviderLease Lease(string id, Guid version, Func<IServiceProvider> factory) {
        return LeaseVersion($"{id.Length}:{id}:{version:N}", factory);
    }

    private ITenantProviderLease LeaseVersion(string id, Func<IServiceProvider> factory) {
        while (true) {
            Entry?       entry   = null;
            Pending?     pending = null;
            List<Entry>? retired = null;
            var          build   = false;
            ITenantProviderLease? lease = null;
            Exception? failure = null;

            try {
                lock (_gate) {
                    ThrowIfDisposed();
                    retired = EvictExpiredLocked();

                    if (_index.TryGetValue(id, out var hit)) {
                        _order.Remove(hit);
                        hit.Value.LastAccess = _time.GetUtcNow().UtcDateTime;
                        _order.AddFirst(hit);
                        hit.Value.ActiveLeases++;
                        entry = hit.Value;
                    } else if (_pending.TryGetValue(id, out pending)) {
                        if (IsConstructing(pending)) {
                            throw new InvalidOperationException($"Tenant provider construction for '{id}' cannot reenter the same key.");
                        }
                    } else {
                        pending = new();
                        _pending.Add(id, pending);
                        build = true;
                    }
                }

                if (entry is not null) {
                    lease = new LeaseHandle(this, entry);
                } else if (build) {
                    lease = BuildLease(id, factory, pending!);
                }
            } catch (Exception error) { failure = error; }

            try { DisposeRetired(retired); }
            catch (Exception error) {
                failure = failure is null ? error : new AggregateException(failure, error);
            }
            if (failure is not null) {
                try { lease?.Dispose(); }
                catch (Exception error) { throw new AggregateException(failure, error); }
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            if (lease is not null) return lease;
            if (pending is null) continue;

            pending.Wait();
        }
    }

    public void Remove(string id) {
        var prefix = $"{id.Length}:{id}:";
        List<Entry>? retired = null;
        lock (_gate) {
            if (_disposed) return;
            foreach (var pair in _pending) {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) pair.Value.Cancelled = true;
            }
            var node = _order.First;
            while (node is not null) {
                var next = node.Next;
                if (node.Value.Id.StartsWith(prefix, StringComparison.Ordinal)) {
                    _index.Remove(node.Value.Id);
                    _order.Remove(node);
                    node.Value.Retired = true;
                    (retired ??= []).Add(node.Value);
                }
                node = next;
            }
        }
        DisposeRetired(retired);
    }

    #endregion

    private ITenantProviderLease BuildLease(string id, Func<IServiceProvider> factory, Pending pending) {
        IServiceProvider provider;
        EnterConstruction(pending);
        try {
            provider = factory();
        } catch {
            CompletePending(id, pending);
            throw;
        } finally {
            ExitConstruction(pending);
        }

        Entry?       entry   = null;
        List<Entry>? evicted = null;
        var          retry   = false;
        lock (_gate) {
            if (!_disposed
                && !pending.Cancelled
                && _pending.TryGetValue(id, out var current)
                && ReferenceEquals(current, pending)) {
                while (_index.Count >= _capacity) {
                    if (!TryEvictOldestLocked(out var victim)) {
                        break;
                    }

                    (evicted ??= []).Add(victim!);
                }

                entry = new(id, provider, _time.GetUtcNow().UtcDateTime) { ActiveLeases = 1 };
                var node = new LinkedListNode<Entry>(entry);
                _order.AddFirst(node);
                _index.Add(id, node);
                _pending.Remove(id);
            } else {
                retry = !_disposed;
                if (_pending.TryGetValue(id, out var remaining) && ReferenceEquals(remaining, pending)) {
                    _pending.Remove(id);
                }
            }

            pending.Complete();
        }

        try { DisposeRetired(evicted); }
        catch (Exception error) {
            try { Release(entry!); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }

        if (entry is not null) {
            return new LeaseHandle(this, entry);
        }

        DisposeProvider(provider);
        if (!retry) {
            throw new ObjectDisposedException(nameof(MemoryCacheTenantProviderCache));
        }

        return LeaseVersion(id, factory);
    }

    private void CompletePending(string id, Pending pending) {
        lock (_gate) {
            if (_pending.TryGetValue(id, out var current) && ReferenceEquals(current, pending)) {
                _pending.Remove(id);
            }

            pending.Complete();
        }
    }

    private List<Entry> RetireAll() {
        lock (_gate) {
            if (_disposed) {
                return [];
            }

            _disposed = true;
            var orphaned = new List<Entry>(_order.Count);
            foreach (var entry in _order) {
                entry.Retired = true;
                if (TryBeginDisposalLocked(entry)) {
                    orphaned.Add(entry);
                }
            }

            foreach (var pending in _pending.Values) {
                pending.Cancelled = true;
                pending.Complete();
            }

            _order.Clear();
            _index.Clear();
            return orphaned;
        }
    }

    private List<Entry>? EvictExpiredLocked() {
        var          threshold = _time.GetUtcNow().UtcDateTime - _ttl;
        List<Entry>? expired   = null;
        var          node      = _order.Last;
        while (node is not null && node.Value.LastAccess < threshold) {
            var prev = node.Previous;
            _order.Remove(node);
            _index.Remove(node.Value.Id);
            node.Value.Retired = true;
            (expired ??= []).Add(node.Value);
            node = prev;
        }

        return expired;
    }

    private bool TryEvictOldestLocked(out Entry? victim) {
        var node = _order.Last;
        if (node is null) {
            victim = null;
            return false;
        }

        _order.Remove(node);
        _index.Remove(node.Value.Id);
        node.Value.Retired = true;
        victim             = node.Value;
        return true;
    }

    private void Release(Entry entry) {
        bool dispose;
        lock (_gate) {
            entry.ActiveLeases--;
            dispose = TryBeginDisposalLocked(entry);
        }

        if (dispose) {
            DisposeProvider(entry.Provider);
        }
    }

    private async ValueTask ReleaseAsync(Entry entry) {
        bool dispose;
        lock (_gate) {
            entry.ActiveLeases--;
            dispose = TryBeginDisposalLocked(entry);
        }

        if (dispose) {
            await DisposeProviderAsync(entry.Provider);
        }
    }

    private void DisposeIfZero(Entry entry) {
        bool dispose;
        lock (_gate) {
            dispose = TryBeginDisposalLocked(entry);
        }

        if (dispose) {
            DisposeProvider(entry.Provider);
        }
    }

    private void DisposeRetired(List<Entry>? entries) {
        if (entries is null) {
            return;
        }

        List<Exception>? failures = null;
        foreach (var entry in entries) {
            try { DisposeIfZero(entry); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    private static void DisposeProviders(List<Entry>? entries) {
        if (entries is null) {
            return;
        }

        List<Exception>? failures = null;
        foreach (var entry in entries) {
            try { DisposeProvider(entry.Provider); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    private static void DisposeProvider(IServiceProvider provider) {
        if (provider is IAsyncDisposable asynchronous) {
            asynchronous.DisposeAsync().AsTask().GetAwaiter().GetResult();
        } else if (provider is IDisposable disposable) {
            disposable.Dispose();
        }
    }

    private static async ValueTask DisposeProviderAsync(IServiceProvider provider) {
        if (provider is IAsyncDisposable asynchronous) {
            await asynchronous.DisposeAsync();
        } else if (provider is IDisposable disposable) {
            disposable.Dispose();
        }
    }

    private static void EnterConstruction(Pending pending) {
        (_constructing ??= []).Add(pending);
    }

    private static void ExitConstruction(Pending pending) { _constructing?.Remove(pending); }

    private static bool IsConstructing(Pending pending) { return _constructing?.Contains(pending) == true; }

    private void ThrowIfDisposed() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(MemoryCacheTenantProviderCache));
        }
    }

    private static bool TryBeginDisposalLocked(Entry entry) {
        if (entry is not { Retired: true, ActiveLeases: 0, Disposing: false }) {
            return false;
        }

        entry.Disposing = true;
        return true;
    }

    #region Nested type: Entry

    private sealed class Entry
    {
        public Entry(string id, IServiceProvider provider, DateTime lastAccess) {
            Id         = id;
            Provider   = provider;
            LastAccess = lastAccess;
        }

        public string           Id           { get; }
        public DateTime         LastAccess   { get; set; }
        public IServiceProvider Provider     { get; }
        public int              ActiveLeases { get; set; }
        public bool             Retired      { get; set; }
        public bool             Disposing    { get; set; }
    }

    #endregion

    #region Nested type: Pending

    private sealed class Pending
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; set; }

        public void Complete() { _completion.TrySetResult(); }

        public void Wait() { _completion.Task.GetAwaiter().GetResult(); }
    }

    #endregion

    #region Nested type: LeaseHandle

    private sealed class LeaseHandle : ITenantProviderLease, IAsyncDisposable
    {
        private readonly MemoryCacheTenantProviderCache _cache;
        private readonly Entry                          _entry;
        private          int                            _released;

        public LeaseHandle(MemoryCacheTenantProviderCache cache, Entry entry) {
            _cache = cache;
            _entry = entry;
        }

        #region ITenantProviderLease Members

        public IServiceProvider Provider => _entry.Provider;

        public void Dispose() {
            if (Interlocked.Exchange(ref _released, 1) != 0) {
                return;
            }

            _cache.Release(_entry);
        }

        public ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _released, 1) != 0) {
                return ValueTask.CompletedTask;
            }

            return _cache.ReleaseAsync(_entry);
        }

        #endregion
    }

    #endregion
}
