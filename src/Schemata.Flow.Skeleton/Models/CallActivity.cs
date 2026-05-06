namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Call Activity — an activity that invokes another reusable
///     <see cref="ProcessDefinition" /> identified by <see cref="CalledElement" />.
///     The called process runs in its own context.
/// </summary>
public sealed class CallActivity : Activity
{
    /// <summary>
    ///     The name of the <see cref="ProcessDefinition" /> to invoke.
    /// </summary>
    public string CalledElement { get; set; } = null!;
}
