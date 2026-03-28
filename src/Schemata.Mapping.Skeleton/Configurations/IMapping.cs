using System;
using System.Linq.Expressions;

namespace Schemata.Mapping.Skeleton.Configurations;

/// <summary>
///     Type-erased representation of a single field mapping or converter, used by mapping engine configurators.
/// </summary>
public interface IMapping
{
    /// <summary>
    ///     The source type of this mapping.
    /// </summary>
    Type SourceType { get; }

    /// <summary>
    ///     The destination type of this mapping.
    /// </summary>
    Type DestinationType { get; }

    /// <summary>
    ///     Whether this mapping is a whole-object converter rather than a field mapping.
    /// </summary>
    bool IsConverter { get; }

    /// <summary>
    ///     Whether this destination field is ignored during mapping.
    /// </summary>
    bool IsIgnored { get; }

    /// <summary>
    ///     Whether a source field expression has been configured.
    /// </summary>
    bool HasSourceField { get; }

    /// <summary>
    ///     The string representation of the destination field expression.
    /// </summary>
    string DestinationFieldName { get; }

    /// <summary>
    ///     Invokes the provided action with the mapping's internal expressions for use by engine configurators.
    /// </summary>
    /// <param name="action">
    ///     Receives the converter expression, destination field, source field, ignore condition, and ignored
    ///     flag.
    /// </param>
    void Invoke(Action<Expression?, Expression?, Expression?, Expression?, bool> action);
}
