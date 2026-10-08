using System;
using System.Collections.Generic;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;

namespace Schemata.Report.Tests.Fixtures;

internal sealed class ReportPersistenceState
{
    private readonly ReportRepositoryTransactions _transactions = new();

    internal List<SchemataReportSnapshot> Snapshots { get; } = [];

    internal List<SchemataReportSnapshotChunk> Chunks { get; } = [];

    internal int CancelAfterChunks { get; set; }

    internal SchemataJobExecution? Execution { get; set; }

    internal int ExecutionRepositoryInstances { get; private set; }

    internal int ExecutionCommitCount { get; private set; }

    internal Queue<DateTime> SuccessfulCaptureTimes { get; } = [];

    internal List<ReportChunkMetadata> ChunkAddSequence { get; } = [];

    internal IRepository<SchemataReportSnapshot> CreateSnapshotRepository() {
        return ReportRepositoryMocks.Create(Snapshots, _transactions, onUpdate: SetSuccessfulCaptureTime);
    }

    internal IRepository<SchemataReportSnapshotChunk> CreateChunkRepository() {
        return ReportRepositoryMocks.Create(Chunks, _transactions,
                                            onCommit: CancelAfterChunkCommit,
                                            onAdd:    CaptureChunkAdd);
    }

    internal IRepository<SchemataJobExecution> CreateExecutionRepository() {
        ExecutionRepositoryInstances++;
        var rows = Execution is null ? [] : new List<SchemataJobExecution> { Execution };
        return ReportRepositoryMocks.Create(rows, _transactions, () => ExecutionCommitCount++);
    }

    internal IRepository<TEntity> CreateRepository<TEntity>(List<TEntity> records)
        where TEntity : class {
        return ReportRepositoryMocks.Create(records, _transactions);
    }

    private void CancelAfterChunkCommit() {
        if (CancelAfterChunks > 0 && Chunks.Count >= CancelAfterChunks && Execution is not null) {
            Execution.State = ExecutionState.Cancelled;
        }
    }

    private void SetSuccessfulCaptureTime(SchemataReportSnapshot snapshot) {
        if (snapshot.State is SnapshotState.Succeeded && SuccessfulCaptureTimes.TryDequeue(out var capturedAt)) {
            snapshot.CapturedAt = capturedAt;
        }
    }


    private void CaptureChunkAdd(SchemataReportSnapshotChunk chunk) {
        ChunkAddSequence.Add(new(
                                 chunk.Name ?? string.Empty,
                                 chunk.Index,
                                 chunk.CanonicalName ?? string.Empty,
                                 chunk.Report,
                                 chunk.Snapshot,
                                 chunk.RowCount));
    }
}