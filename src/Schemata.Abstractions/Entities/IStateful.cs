namespace Schemata.Abstractions.Entities;

/// <summary>
///     Declares a discrete state field representing a workflow or lifecycle
///     stage, corresponding to
///     <seealso href="https://google.aip.dev/216">AIP-216: States</seealso>
///     <c>state</c>.
/// </summary>
public interface IStateful
{
    /// <summary>
    ///     The current state of the entity.
    /// </summary>
    string? State { get; set; }
}
