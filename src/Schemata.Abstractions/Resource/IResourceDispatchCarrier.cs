namespace Schemata.Abstractions.Resource;

/// <summary>
///     A dispatch object that can carry the identified dispatch's
///     <see cref="ResourceDispatchExecution" /> between the idempotency wrap and the mutation
///     owner. Standard-verb envelopes implement it; AIP-136 custom-method request DTOs implement
///     it to make their methods idempotency-eligible and to receive the execution their handler
///     reports completion through.
/// </summary>
public interface IResourceDispatchCarrier
{
    /// <summary>
    ///     The execution attached by the idempotency wrap, or <see langword="null" /> when the
    ///     dispatch runs unidentified.
    /// </summary>
    ResourceDispatchExecution? Dispatch { get; set; }
}
