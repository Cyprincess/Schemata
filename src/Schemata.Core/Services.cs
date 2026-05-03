using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core;

/// <summary>
///     In-memory <see cref="IServiceCollection" /> backing the staging service
///     collection on <see cref="SchemataBuilder" />. Not thread-safe; used during
///     application construction only.
/// </summary>
internal class Services : IServiceCollection
{
    private readonly List<ServiceDescriptor> _services = [];

    #region IServiceCollection Members

    /// <inheritdoc />
    public IEnumerator<ServiceDescriptor> GetEnumerator() { return _services.GetEnumerator(); }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

    /// <inheritdoc />
    public void Add(ServiceDescriptor item) { _services.Add(item); }

    /// <inheritdoc />
    public void Clear() { _services.Clear(); }

    /// <inheritdoc />
    public bool Contains(ServiceDescriptor item) { return _services.Contains(item); }

    /// <inheritdoc />
    public void CopyTo(ServiceDescriptor[] array, int arrayIndex) { _services.CopyTo(array, arrayIndex); }

    /// <inheritdoc />
    public bool Remove(ServiceDescriptor item) { return _services.Remove(item); }

    /// <inheritdoc />
    public int Count => _services.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public int IndexOf(ServiceDescriptor item) { return _services.IndexOf(item); }

    /// <inheritdoc />
    public void Insert(int index, ServiceDescriptor item) { _services.Insert(index, item); }

    /// <inheritdoc />
    public void RemoveAt(int index) { _services.RemoveAt(index); }

    /// <inheritdoc />
    public ServiceDescriptor this[int index]
    {
        get => _services[index];
        set => _services[index] = value;
    }

    #endregion
}
