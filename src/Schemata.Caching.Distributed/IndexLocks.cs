using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Caching.Skeleton;

namespace Schemata.Caching.Distributed;

/// <summary>
///     Striped in-process lock set that serializes read-modify-write on a collection
///     to prevent lost writes.
/// </summary>
/// <remarks>
///     <para>
///         Bounded to <see cref="StripeCount" /> semaphores regardless of the number of distinct keys, so
///         memory stays constant. Unrelated keys may share a stripe and serialize incidentally — this is
///         intentional; the lock exists to prevent lost writes, not to maximize throughput.
///     </para>
///     <para>
///         When multiple processes share a distributed cache backend, this lock does not provide
///         cross-process serialization. Use Redis or NCache implementations of
///         <see cref="ICacheProvider" /> for cluster-safe collection operations.
///     </para>
/// </remarks>
public static class IndexLocks
{
    private const           int             StripeCount = 64;
    private static readonly SemaphoreSlim[] Stripes     = CreateStripes();

    /// <summary>
    ///     Acquires the lock semaphore for <paramref name="key" /> and returns a release handle.
    /// </summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A disposable handle that releases the semaphore when disposed.</returns>
    public static async Task<IDisposable> AcquireAsync(string key, CancellationToken ct) {
        var slot = (int)((uint)key.GetHashCode() % StripeCount);
        var sem  = Stripes[slot];
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private static SemaphoreSlim[] CreateStripes() {
        var stripes = new SemaphoreSlim[StripeCount];
        for (var i = 0; i < StripeCount; i++) {
            stripes[i] = new(1, 1);
        }

        return stripes;
    }

    #region Nested type: Releaser

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _sem;

        public Releaser(SemaphoreSlim sem) { _sem = sem; }

        #region IDisposable Members

        public void Dispose() { _sem.Release(); }

        #endregion
    }

    #endregion
}
