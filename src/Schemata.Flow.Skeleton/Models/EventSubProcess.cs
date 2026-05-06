namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Event Sub-Process — a sub-process triggered by a start event
///     (Message, Timer, Error, Signal, etc.) rather than by sequence flow.
///     Can be interrupting (cancels the parent scope) or non-interrupting
///     (runs concurrently).
/// </summary>
public sealed class EventSubProcess : SubProcess
{ }
