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
///     in fixture setups without a dispatcher, the same call falls back to the in-process
///     timer so legacy single-process usage keeps working without dispatcher wiring.
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
                    entry.Cts.Cancel();
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

        /// <summary>The scheduled job descriptor this entry tracks.</summary>
        public SchemataJob             Job { get; }

        /// <summary>Cancellation source signalled when the job is unscheduled or the scheduler stops.</summary>
        public CancellationTokenSource Cts { get; }

        /// <summary>
        ///     Context pre-built by <see cref="DefaultScheduler.TriggerAsync{TJob}" />.
        ///     When set, the timer fire path uses it instead of constructing a
        ///     fresh context, preserving the caller-supplied execution UID.
        /// </summary>
        public JobContext?             PreparedContext { get; }
    }
}
