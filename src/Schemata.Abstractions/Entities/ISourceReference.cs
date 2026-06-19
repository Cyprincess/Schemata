namespace Schemata.Abstractions.Entities;

/// <summary>
///     Optional weak-consistency reference from a derived row (event, flow process, audit
///     entry) back to the originating business entity. Carriers store
///     <see cref="SourceType" /> (CLR full name), <see cref="Source" />
///     (AIP-122 canonical resource name), and <see cref="SourceTimestamp" /> (the source
///     entity's <c>IConcurrency.Timestamp</c> at capture time). Consumers can compare the
///     captured timestamp against the source entity's current timestamp to detect drift
///     while keeping the derived row compact.
/// </summary>
/// <remarks>
///     All three properties are nullable so derived rows that have no semantic source (system
///     events, external webhooks, plain notifications) can keep them empty. The framework
///     populates them automatically when the publisher passes the source entity through the
///     corresponding bus overload.
/// </remarks>
public interface ISourceReference
{
    /// <summary>
    ///     The CLR <c>Type.FullName</c> of the source business entity, or <see langword="null" />
    ///     when this row has no semantic source.
    /// </summary>
    string? SourceType { get; set; }

    /// <summary>
    ///     The source entity's <c>ICanonicalName.CanonicalName</c> (AIP-122), or
    ///     <see langword="null" /> when the source has none.
    /// </summary>
    string? Source { get; set; }

    /// <summary>
    ///     The source entity's <c>IConcurrency.Timestamp</c> snapshot at capture time, or
    ///     <see langword="null" /> for sources outside <c>IConcurrency</c>.
    /// </summary>
    System.Guid? SourceTimestamp { get; set; }
}
