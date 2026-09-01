using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Request body and command for the <c>:run</c> custom method on
///     <see cref="Entities.SchemataJob" />.
/// </summary>
public sealed class RunJobRequest : ICanonicalName, ICommand<Operation>, IRequestPrincipal
{
    /// <summary>
    ///     Variables copied to <see cref="JobContext.Variables" /> for this trigger.
    ///     When omitted, the execution receives an empty dictionary; the handler does not merge
    ///     values from the persisted job row.
    /// </summary>
    public Dictionary<string, string?>? Variables { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    #endregion
}
