//
// Copyright (c) 2011 Google, Inc.
// Copyright (c) 2014 Gustavo J Knuppe (https://github.com/knuppe)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sub-license, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
// CityHash, by Geoff Pike and Jyrki Alakuijala
//
// Ported to C# by Gustavo J Knuppe (https://github.com/knuppe)
//
//   - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
//   - May you do good and not evil.                                         -
//   - May you find forgiveness for yourself and forgive others.             -
//   - May you share freely, never taking more than you give.                -
//   - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
//
// Project site: https://github.com/knuppe/cityhash
// Original code: https://code.google.com/p/cityhash/
//

using System;

namespace Schemata.Common.Hash;

/// <summary>
/// Represents a 128-bit unsigned integer.
/// </summary>
public struct Uint128 : IEquatable<Uint128> {
    /// <summary>
    /// Initializes a 128-bit unsigned integer from its low and high 64-bit halves.
    /// </summary>
    /// <param name="low">The low-order 64 bits.</param>
    /// <param name="high">The high-order 64 bits.</param>
    public Uint128(ulong low, ulong high)
        : this() {
        Low  = low;
        High = high;
    }

    #region . Properties .

    #region . Low .
    /// <summary>
    /// Gets or sets the low-order 64-bits.
    /// </summary>
    /// <value>The low-order 64-bits.</value>
    public ulong Low { get; set; }
    #endregion

    #region . High .
    /// <summary>
    /// Gets or sets the high-order 64-bits.
    /// </summary>
    /// <value>The high-order 64-bits.</value>
    public ulong High { get; set; }
    #endregion

    #endregion

    #region . Equals .
    public bool Equals(Uint128 other) {
        return Low == other.Low && High == other.High;
    }

    public override bool Equals(object? obj) {
        if (ReferenceEquals(null, obj)) return false;
        return obj is Uint128 uint128 && Equals(uint128);
    }
    #endregion

    #region . GetHashCode .
    public override int GetHashCode() {
        unchecked {
            return (Low.GetHashCode() * 397) ^ High.GetHashCode();
        }
    }


    public override string ToString() => High.ToString("X16") + Low.ToString("X16");
    #endregion

}