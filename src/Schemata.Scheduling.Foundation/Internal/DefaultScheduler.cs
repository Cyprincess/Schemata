using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

/// <summary>
///     In-memory <see cref="IScheduler" /> acting as a materializer and timer. Every fire — cron,
///     periodic, one-time, <see cref="TriggerAsync{TJob}" />, or durable operation — persists a
///     <see cref="ExecutionState.Pending" /> <see cref="SchemataJobExecution" /> row up front,
///     carrying its due time in <see cref="SchemataJobExecution.StartTime" />. An in-memory timer
///     signals <see cref="JobExecutionDispatcher" /> when a row comes due; the dispatcher is the
///     single executor that runs the job body and advances recurring schedules. The scheduler never
///     runs a job body itself.
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

    internal int EntryCount => _entries.Count;

    public DefaultScheduler(
        IServiceProvider                    services,
        IOptions<SchemataSchedulingOptions> options,
        ILogger<DefaultScheduler>?          logger       = null,
        TimeProvider?                       time = null
    ) {
        _services = services;
        _options  = options;
        _logger   = logger;
        _time     = time ?? TimeProvider.System;
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

    /// <summary>Signals the dispatcher that a row has come due, when one is registered.</summary>
    private void SignalDispatcher() {
        _services.GetService<JobExecutionDispatcher>()?.NotifyPending();
    }

    private sealed class ScheduledEntry
    {
        public ScheduledEntry(SchemataJob job, CancellationTokenSource cts, int replayedMisses = 0) {
            Job            = job;
            Cts            = cts;
            ReplayedMisses = replayedMisses;
        }

        public SchemataJob             Job { get; }

        public CancellationTokenSource Cts { get; }

        public int ReplayedMisses { get; }
    }
}
