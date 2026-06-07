namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Receive Task — an activity that waits for a message to arrive
///     before completing. May also serve as a process instantiation point
///     when used with a Message Start Event.
/// </summary>
public sealed class ReceiveTask : Activity;
