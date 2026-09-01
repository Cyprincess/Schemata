using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime.SubProcesses;

/// <summary>Terminal child-process status observed when a <see cref="CallActivity" /> can resume its parent token.</summary>
public sealed record CallActivityCompletion(
    string ChildProcess,
    string State,
    bool   Failed);