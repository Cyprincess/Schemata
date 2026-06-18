namespace Schemata.Abstractions.Resource;

/// <summary>
///     Resolves a durable <see cref="OperationDescriptor" /> from its stable key.
///     Populated at startup from code-registered descriptors.
/// </summary>
public interface IOperationRegistry
{
    /// <summary>Returns the descriptor for <paramref name="key" />, or throws if none is registered.</summary>
    OperationDescriptor GetRequired(string key);
}
