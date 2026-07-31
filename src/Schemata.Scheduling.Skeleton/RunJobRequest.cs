using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Request body for the <c>:run</c> custom method on
///     <see cref="Entities.SchemataJob" />. Carries an
///     <see cref="ICanonicalName" /> identity so it satisfies the
///     <c>IResourceMethodHandler</c> constraint.
/// </summary>
public sealed class RunJobRequest : ICanonicalName
{
    /// <summary>
    ///     Variables forwarded to <see cref="JobContext.Variables" /> for this
    ///     trigger. Overrides any defaults persisted on the job row.
    /// </summary>
    public Dictionary<string, string?>? Variables { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
