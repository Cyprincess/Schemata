using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

/// <summary>
///     In-memory <see cref="IScheduler" />. Cron/periodic fires advance an in-process timer
///     loop and delegate audit through <see cref="IJobLifecycleObserver" /> implementations.
///     <see cref="TriggerAsync{TJob}" /> persists a <see cref="SchemataJobExecution" /> row
///     and hands execution to <see cref="JobExecutionDispatcher" /> when one is registered;
///     fixture setups that omit a dispatcher fall back to the in-process timer.
/// </summary>
public sealed partial class DefaultScheduler : IScheduler
{
    private readonly SemaphoreSlim                                _lock    = new(1, 1);
    private readonly ILogger<DefaultScheduler>?                   _logger;
    private readonly IOptions<SchemataSchedulingOptions>          _options;
    private readonly IServiceProvider                             _services;
    private readonly ConcurrentDictionary<string, ScheduledEntry> _entries = new();
    private readonly TimeProvider                                 _time;
    private          bool                                         _stopped = true;

    public DefaultScheduler(
        IServiceProvider                    services,
        IOptions<SchemataSchedulingOptions> options,
        ILogger<DefaultScheduler>?          logger       = null,
        TimeProvider?                       timeProvider = null
    ) {
        _services = services;
        _options  = options;
        _logger   = logger;
        _time     = timeProvider ?? TimeProvider.System;
    }

    #region IScheduler Members

    public async Task StartAsync(CancellationToken ct) {
        await _lock.WaitAsync(ct);
        try {
            _stopped = false;
        } finally {
            _lock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct) {
        await _lock.WaitAsync(CancellationToken.None);
        try {
            _stopped = true;
            foreach (var entry in _entries.Values) {
                try {
                    await entry.Cts.CancelAsync();
                } catch (ObjectDisposedException) {
                    // Another path already disposed this entry; safe to swallow.
                }
                entry.Cts.Dispose();
            }
            _entries.Clear();
        } finally {
            _lock.Release();
        }
    }

    #endregion

    private sealed class ScheduledEntry
    {
        public ScheduledEntry(SchemataJob job, CancellationTokenSource cts, JobContext? preparedContext = null) {
            Job             = job;
            Cts             = cts;
            PreparedContext = preparedContext;
        }

        public SchemataJob             Job { get; }

        public CancellationTokenSource Cts { get; }

        public JobContext?             PreparedContext { get; }
    }
}
