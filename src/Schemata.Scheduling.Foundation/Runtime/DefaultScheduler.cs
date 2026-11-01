using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Runtime;

/// <summary>
///     In-memory <see cref="IScheduler{TJob,TExecution}" /> acting as a materializer and timer.
///     Every fire — cron, periodic, one-time, <see cref="IScheduler.TriggerAsync{TJob}" />, or
///     durable operation — persists an <see cref="ExecutionState.Pending" />
///     <typeparamref name="TExecution" /> row up front, carrying its due time in
///     <see cref="SchemataJobExecution.StartTime" />. An in-memory timer signals
///     <see cref="JobExecutionDispatcher{TJob,TExecution}" /> when a row comes due; the dispatcher
///     is the single executor that runs the job body and advances recurring schedules. The
///     scheduler never runs a job body itself.
/// </summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public sealed partial class DefaultScheduler<TJob, TExecution> : IScheduler<TJob, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    private readonly SemaphoreSlim                                _lock    = new(1, 1);
    private readonly ILogger<DefaultScheduler<TJob, TExecution>>? _logger;
    private readonly IOptions<SchemataSchedulingOptions>          _options;
    private readonly IServiceProvider                             _services;
    private readonly ConcurrentDictionary<string, ScheduledEntry> _entries = new();
    private readonly TimeProvider                                 _time;
    private          bool                                         _stopped = true;

    internal int EntryCount => _entries.Count;

    internal ConcurrentDictionary<string, ScheduledEntry> Entries => _entries;

    internal SemaphoreSlim Gate => _lock;

    internal ILogger<DefaultScheduler<TJob, TExecution>>? Logger => _logger;

    internal IOptions<SchemataSchedulingOptions> Options => _options;

    internal IServiceProvider Services => _services;

    internal bool IsStopped => _stopped;

    internal TimeProvider Time => _time;

    public DefaultScheduler(
        IServiceProvider                             services,
        IOptions<SchemataSchedulingOptions>          options,
        ILogger<DefaultScheduler<TJob, TExecution>>? logger       = null,
        TimeProvider?                                time = null
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
    internal void SignalDispatcher() {
        _services.GetService<JobExecutionDispatcher<TJob, TExecution>>()?.NotifyPending();
    }

    internal sealed class ScheduledEntry
    {
        public ScheduledEntry(TJob job, CancellationTokenSource cts, int replayedMisses = 0, string? key = null) {
            Job            = job;
            Cts            = cts;
            Token          = cts.Token;
            ReplayedMisses = replayedMisses;
            Key            = key;
        }

        public TJob                    Job { get; }

        public CancellationTokenSource Cts { get; }

        public CancellationToken Token { get; }

        public int ReplayedMisses { get; }

        /// <summary>
        ///     Key this entry is registered under in <see cref="Entries" />, or
        ///     <see langword="null" /> for entries built outside the timer-arming support.
        /// </summary>
        public string? Key { get; }
    }
}
