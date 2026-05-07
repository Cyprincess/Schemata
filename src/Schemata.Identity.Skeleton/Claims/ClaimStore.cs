using System.Collections;
using System.Collections.Generic;

namespace Schemata.Identity.Skeleton.Claims;

/// <summary>
///     An ordered collection of string values representing a single claim type's values.
/// </summary>
/// <remarks>
///     Null or whitespace values are silently ignored on <see cref="Add" />.
///     Serialized as a JSON array (or a single string when the count is one) via
///     <see cref="Schemata.Identity.Skeleton.Json.ClaimStoreJsonConverter" />.
/// </remarks>
public sealed class ClaimStore : IList<string>
{
    private readonly List<string> _values = [];

    #region IList<string> Members

    /// <inheritdoc />
    /// <remarks>Null or whitespace values are silently ignored.</remarks>
    public void Add(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        _values.Add(value!);
    }

    /// <inheritdoc />
    public void Clear() { _values.Clear(); }

    /// <inheritdoc />
    public bool Contains(string item) { return _values.Contains(item); }

    /// <inheritdoc />
    public void CopyTo(string[] array, int arrayIndex) { _values.CopyTo(array, arrayIndex); }

    /// <inheritdoc />
    public bool Remove(string item) { return _values.Remove(item); }

    /// <inheritdoc />
    public int Count => _values.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() { return _values.GetEnumerator(); }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

    /// <inheritdoc />
    public int IndexOf(string item) { return _values.IndexOf(item); }

    /// <inheritdoc />
    public void Insert(int index, string item) { _values.Insert(index, item); }

    /// <inheritdoc />
    public void RemoveAt(int index) { _values.RemoveAt(index); }

    /// <inheritdoc />
    public string this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }

    #endregion
}
