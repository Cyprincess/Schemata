using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares a resource API with all four type roles explicitly separated, per
///     <seealso href="https://google.aip.dev/121">AIP-121: Resource-oriented design</seealso>
///     and <seealso href="https://google.aip.dev/123">AIP-123: Resource types</seealso>.
///     This is the canonical generic form of <see cref="ResourceAttribute" />.
/// </summary>
/// <typeparam name="TEntity">The persistence entity.</typeparam>
/// <typeparam name="TRequest">The DTO for create and update requests.</typeparam>
/// <typeparam name="TDetail">The DTO for single-resource read responses.</typeparam>
/// <typeparam name="TSummary">The DTO for each list-result item.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity, TRequest, TDetail, TSummary> : ResourceAttribute
{
    /// <inheritdoc />
    public ResourceAttribute() : base(typeof(TEntity), typeof(TRequest), typeof(TDetail), typeof(TSummary)) { }
}
