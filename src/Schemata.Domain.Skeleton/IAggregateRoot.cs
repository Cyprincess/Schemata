using Schemata.Abstractions.Entities;

namespace Schemata.Domain.Skeleton;

/// <summary>
///     Marks the entity that owns a consistency boundary: the one object a transaction loads,
///     mutates and saves as a unit.
/// </summary>
/// <remarks>
///     Composed from the existing <see cref="IIdentifier" /> and <see cref="IConcurrency" /> traits
///     rather than declaring members of its own, so an aggregate is an ordinary Schemata entity that
///     the repository advisors already understand. <see cref="IConcurrency" /> is not optional here:
///     a consistency boundary that cannot detect a concurrent write is not one.
/// </remarks>
public interface IAggregateRoot : IIdentifier, IConcurrency;
