using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Schemata.Modeling.Generator.Expressions;

public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) { _array = array; }

    public int Length => _array.IsDefault ? 0 : _array.Length;

    public T this[int index] => _array[index];

    #region IEnumerable<T> Members

    public ImmutableArray<T>.Enumerator GetEnumerator() {
        return _array.IsDefault ? ImmutableArray<T>.Empty.GetEnumerator() : _array.GetEnumerator();
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator() {
        return (_array.IsDefault ? ImmutableArray<T>.Empty : _array).AsEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() { return ((IEnumerable<T>)this).GetEnumerator(); }

    #endregion

    #region IEquatable<EquatableArray<T>> Members

    public bool Equals(EquatableArray<T> other) {
        if (_array.IsDefault && other._array.IsDefault) {
            return true;
        }

        if (_array.IsDefault || other._array.IsDefault) {
            return false;
        }

        return _array.SequenceEqual(other._array);
    }

    #endregion

    public override bool Equals(object? obj) { return obj is EquatableArray<T> other && Equals(other); }

    public override int GetHashCode() {
        if (_array.IsDefault) {
            return 0;
        }

        unchecked {
            var hash = 17;
            foreach (var item in _array) {
                hash = hash * 31 + item?.GetHashCode() ?? 0;
            }

            return hash;
        }
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) { return left.Equals(right); }

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) { return !left.Equals(right); }

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) { return new(array); }

    public static implicit operator ImmutableArray<T>(EquatableArray<T> array) {
        return array._array.IsDefault ? ImmutableArray<T>.Empty : array._array;
    }
}
