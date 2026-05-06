using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Associates a loaded <see cref="ProcessDefinition" /> with its
///     engine name and configuration.
/// </summary>
public sealed class ProcessRegistration
{
    /// <summary>
    ///     The registered name of the process definition.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    ///     The engine name (<c>"StateMachine"</c> or <c>"Bpmn"</c>) that
    ///     should execute instances of this process.
    /// </summary>
    public string Engine { get; set; } = null!;

    /// <summary>
    ///     The loaded process definition AST.
    /// </summary>
    public ProcessDefinition Definition { get; set; } = null!;

    /// <summary>
    ///     The configuration used to register this process.
    /// </summary>
    public ProcessConfiguration Configuration { get; set; } = null!;
}
