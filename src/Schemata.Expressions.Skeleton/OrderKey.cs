using System.Collections.Generic;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     A parsed order-by segment: the member path to sort on and its direction.
/// </summary>
/// <param name="Path">The member path segments, in wire format.</param>
/// <param name="Descending">Whether the segment sorts in descending order.</param>
public sealed record OrderKey(IReadOnlyList<string> Path, bool Descending);
