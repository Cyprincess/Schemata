namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Call Activity - an activity that invokes another reusable
///     <see cref="ProcessDefinition" /> identified by <see cref="CalledElement" />.
///     The called process runs in its own context.
/// </summary>
public sealed class CallActivity : Activity
{
    private string _calledElement = null!;
    private string _definitionVersion = "1";

    /// <summary>
    ///     The name of the <see cref="ProcessDefinition" /> to invoke.
    /// </summary>
    public string CalledElement {
        get => _calledElement;
        set {
            EnsureMutable();
            _calledElement = value;
        }
    }

    public string DefinitionVersion {
        get => _definitionVersion;
        set {
            EnsureMutable();
            _definitionVersion = value;
        }
    }
}
