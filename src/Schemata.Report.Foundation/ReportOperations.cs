using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Foundation;

/// <summary>
///     AIP-136 custom-method verb constants for Report operations, matching the verbs declared by the
///     Report resource registrations and carried by the
///     <see cref="Schemata.Messaging.Skeleton.Commands.ResourceMethodRequest{TEntity,TRequest,TResponse}" />
///     envelopes.
/// </summary>
public static class ReportOperations
{
    /// <summary>Runs a report definition or inline query.</summary>
    public const string Run = Verbs.Run;

    /// <summary>Generates a report snapshot or inline result.</summary>
    public const string Generate = Verbs.Generate;

    /// <summary>Reads a page of report snapshot rows.</summary>
    public const string Read = Verbs.Read;
}
