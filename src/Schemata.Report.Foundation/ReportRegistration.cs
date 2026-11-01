using System;
using Schemata.Common.Errors;

namespace Schemata.Report.Foundation;

/// <summary>The selected entity triple and the guard shared by Report capability boundaries.</summary>
public sealed class ReportRegistration(Type report, Type snapshot, Type chunk)
{
    public Type Report { get; } = report;

    public Type Snapshot { get; } = snapshot;

    public Type Chunk { get; } = chunk;

    private readonly (Type Report, Type Snapshot, Type Chunk)? _conflict;

    private ReportRegistration(ReportRegistration selected, Type report, Type snapshot, Type chunk)
        : this(selected.Report, selected.Snapshot, selected.Chunk) {
        _conflict = (report, snapshot, chunk);
    }

    internal ReportRegistration Select(Type report, Type snapshot, Type chunk) {
        return _conflict is not null || (Report == report && Snapshot == snapshot && Chunk == chunk)
            ? this
            : new(this, report, snapshot, chunk);
    }

    public void EnsureSingleTriple<TEntity>() {
        if (_conflict is not null) {
            throw SchemataResourceErrors.PreconditionFailed<TEntity>(
                description: "Conflicting Report entity triples are registered for this host; report operations require a single triple.");
        }
    }
}
