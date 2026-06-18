using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     LRU <see cref="ITenantProviderCache" /> with capacity-bound synchronous eviction
///     and sliding expiration. Disposal of retired providers is deferred until every
///     outstanding lease has been released so active tenant scopes are never disposed
///     out from under their callers.
/// </summary>
public sealed class MemoryCacheTenantProviderCache : ITenantProviderCache, IDisposable
{
    private readonly int                                       _capacity;
    private readonly object                                    _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index;
    private readonly LinkedList<Entry>                         _order = new();
    private readonly TimeProvider                              _time;
    private readonly TimeSpan                                  _ttl;
    private          bool                                      _disposed;

    /// <summary>
    ///     Initializes a new instance with capacity and sliding expiration sourced from <see cref="SchemataTenancyOptions" />.
    /// </summary>
    /// <param name="options">Tenancy options supplying capacity and sliding expiration.</param>
    /// <param name="timeProvider">Clock used for sliding-expiration eviction; defaults to the system clock.</param>
    public MemoryCacheTenantProviderCache(IOptions<SchemataTenancyOptions> options, TimeProvider? timeProvider = null) {
        _ttl      = options.Value.ProviderSlidingExpiration;
        _capacity = options.Value.ProviderMaxCapacity;
        _index    = new(_capacity);
        _time     = timeProvider ?? TimeProvider.System;
    }

    #region IDisposable Members

    public void Dispose() {
        List<Entry> orphaned;
        lock (_gate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
            orphaned  = new(_order.Count);
            foreach (var entry in _order) {
                entry.Retired = true;
                if (entry.ActiveLeases == 0) {
                    orphaned.Add(entry);
                }
            }

            _order.Clear();
            _index.Clear();
        }

        foreach (var entry in orphaned) {
            DisposeProvider(entry);
        }
    }

    #endregion

    #region ITenantProviderCache Members

    public ITenantProviderLease Lease(string id, Func<IServiceProvider> factory) {
        Entry?       entry   = null;
        List<Entry>? evicted = null;
        try {
            lock (_gate) {
                if (_disposed) {
                    throw new ObjectDisposedException(nameof(MemoryCacheTenantProviderCache));
                }

                evicted = EvictExpiredLocked();

                if (_index.TryGetValue(id, out var hit)) {
                    _order.Remove(hit);
                    hit.Value.LastAccess = _time.GetUtcNow().UtcDateTime;
                    _order.AddFirst(hit);
                    hit.Value.ActiveLeases++;
                    entry = hit.Value;
                } else {
                    // Build the replacement before evicting so a factory failure cannot retire a
                    // healthy provider for an entry that never gets added.
                    var provider = factory();

                    while (_index.Count >= _capacity) {
                        if (!TryEvictOldestLocked(out var victim)) {
                            break;
                        }

                        (evicted ??= []).Add(victim!);
                    }

                    var fresh = new Entry(id, provider, _time.GetUtcNow().UtcDateTime);
                    var node  = new LinkedListNode<Entry>(fresh);
                    _order.AddFirst(node);
                    _index[id] = node;
                    fresh.ActiveLeases++;
                    entry = fresh;
                }
            }

            return new LeaseHandle(this, entry);
        } finally {
            if (evicted is not null) {
                foreach (var victim in evicted) {
                    DisposeIfZero(victim);
                }
            }
        }
    }

    public void Remove(string id) {
        Entry? entry = null;
        lock (_gate) {
            if (_disposed) {
                return;
            }

            if (!_index.TryGetValue(id, out var node)) {
                return;
            }

            _index.Remove(id);
            _order.Remove(node);
            node.Value.Retired = true;
            entry              = node.Value;
        }

        DisposeIfZero(entry);
    }

    #endregion

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
            dispose = entry.Retired && entry.ActiveLeases == 0;
        }

        if (dispose) {
            DisposeProvider(entry);
        }
    }

    private void DisposeIfZero(Entry entry) {
        bool dispose;
        lock (_gate) {
            dispose = entry.ActiveLeases == 0;
        }

        if (dispose) {
            DisposeProvider(entry);
        }
    }

    private static void DisposeProvider(Entry entry) {
        if (entry.Provider is IDisposable disposable) {
            disposable.Dispose();
        }
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
    }

    #endregion

    #region Nested type: LeaseHandle

    private sealed class LeaseHandle : ITenantProviderLease
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

        #endregion
    }

    #endregion
}
