using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Resource;

namespace Schemata.Scheduling.Foundation.Internal;

/// <summary>
///     Builds a lookup of code-registered <see cref="OperationDescriptor" /> instances.
///     Persisted job rows carry only the stable key, so the descriptor's argument type
///     is never loaded from persisted data.
/// </summary>
internal sealed class DefaultOperationRegistry : IOperationRegistry
{
    private readonly IReadOnlyDictionary<string, OperationDescriptor> _descriptors;

    public DefaultOperationRegistry(IEnumerable<OperationDescriptor> descriptors) {
        _descriptors = descriptors.ToDictionary(descriptor => descriptor.Key);
    }

    #region IOperationRegistry Members

    public OperationDescriptor GetRequired(string key) {
        return _descriptors.TryGetValue(key, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"No operation is registered for key '{key}'.");
    }

    #endregion
}
