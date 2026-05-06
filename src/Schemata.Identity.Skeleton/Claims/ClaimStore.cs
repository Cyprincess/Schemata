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

    /// <remarks>Null or whitespace values are silently ignored.</remarks>
    public void Add(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        _values.Add(value!);
    }

    public void Clear() { _values.Clear(); }

    public bool Contains(string item) { return _values.Contains(item); }

    public void CopyTo(string[] array, int arrayIndex) { _values.CopyTo(array, arrayIndex); }

    public bool Remove(string item) { return _values.Remove(item); }

    public int Count => _values.Count;

    public bool IsReadOnly => false;

    public IEnumerator<string> GetEnumerator() { return _values.GetEnumerator(); }

    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

    public int IndexOf(string item) { return _values.IndexOf(item); }

    public void Insert(int index, string item) { _values.Insert(index, item); }

    public void RemoveAt(int index) { _values.RemoveAt(index); }

    public string this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }

    #endregion
}
