namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Repetition shape of an <see cref="Activity" />, flattened for the definition projection.
///     Absent characteristics project as <see langword="null" />.
/// </summary>
public enum LoopKind
{
    /// <summary>A <see cref="StandardLoopCharacteristics" /> while/until loop.</summary>
    Standard,

    /// <summary>A <see cref="MultiInstanceLoopCharacteristics" /> running one instance at a time.</summary>
    SequentialMultiInstance,

    /// <summary>A <see cref="MultiInstanceLoopCharacteristics" /> running every instance concurrently.</summary>
    ParallelMultiInstance,
}
