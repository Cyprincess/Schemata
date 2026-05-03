using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     LRU <see cref="ITenantProviderCache" /> with capacity-bound synchronous eviction
///     and sliding expiration. Disposes evicted providers deterministically.
/// </summary>
public sealed class MemoryCacheTenantProviderCache : ITenantProviderCache, IDisposable
{
    private readonly object                                    _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index;
    private readonly int                                       _capacity;
    private readonly LinkedList<Entry>                         _order = new();
    private readonly TimeSpan                                  _ttl;

    /// <summary>
    ///     Initializes a new instance with capacity and sliding expiration sourced from <see cref="SchemataTenancyOptions" />.
    /// </summary>
    public MemoryCacheTenantProviderCache(IOptions<SchemataTenancyOptions> options) {
        _ttl      = options.Value.ProviderSlidingExpiration;
        _capacity = options.Value.ProviderMaxCapacity;
        _index    = new(_capacity);
    }

    #region IDisposable Members

    /// <inheritdoc />
    public void Dispose() {
        lock (_gate) {
            foreach (var entry in _order) {
                (entry.Provider as IDisposable)?.Dispose();
            }

            _order.Clear();
            _index.Clear();
        }
    }

    #endregion

    #region ITenantProviderCache Members

    /// <inheritdoc />
    public IServiceProvider GetOrAdd(string id, Func<IServiceProvider> factory) {
        lock (_gate) {
            EvictExpired();

            if (_index.TryGetValue(id, out var hit)) {
                _order.Remove(hit);
                hit.Value.LastAccess = DateTime.UtcNow;
                _order.AddFirst(hit);
                return hit.Value.Provider;
            }

            while (_index.Count >= _capacity) {
                var victim = _order.Last;
                if (victim is null) {
                    break;
                }

                _order.RemoveLast();
                _index.Remove(victim.Value.Id);
                (victim.Value.Provider as IDisposable)?.Dispose();
            }

            var provider = factory();
            var node     = new LinkedListNode<Entry>(new(id, provider, DateTime.UtcNow));
            _order.AddFirst(node);
            _index[id] = node;
            return provider;
        }
    }

    /// <inheritdoc />
    public void Remove(string id) {
        lock (_gate) {
            if (!_index.TryGetValue(id, out var node)) {
                return;
            }

            _index.Remove(id);
            _order.Remove(node);
            (node.Value.Provider as IDisposable)?.Dispose();
        }
    }

    #endregion

    private void EvictExpired() {
        var threshold = DateTime.UtcNow - _ttl;
        var node      = _order.Last;
        while (node is not null && node.Value.LastAccess < threshold) {
            var prev = node.Previous;
            _order.Remove(node);
            _index.Remove(node.Value.Id);
            (node.Value.Provider as IDisposable)?.Dispose();
            node = prev;
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

        public string           Id         { get; }
        public DateTime         LastAccess { get; set; }
        public IServiceProvider Provider   { get; }
    }

    #endregion
}
