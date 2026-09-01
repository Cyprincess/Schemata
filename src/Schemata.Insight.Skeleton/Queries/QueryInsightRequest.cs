using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Insight.Skeleton.Models;

namespace Schemata.Insight.Skeleton.Queries;

/// <summary>A federated read query: sources, joins, an ordered transformation pipeline, and nested selections.</summary>
public sealed class QueryInsightRequest : IInsightQuery<QueryInsightResponse>
{
    /// <summary>The bound sources (at least one); FROM/JOIN inputs.</summary>
    public IList<SourceBinding> Sources { get; set; } = [];

    /// <summary>Explicit cross-source joins.</summary>
    public IList<JoinSpec> Joins { get; set; } = [];

    /// <summary>The OData <c>$apply</c>-style transformation pipeline applied in order.</summary>
    public IList<TransformationSpec> Transformations { get; set; } = [];

    /// <summary>The GraphQL-style nested projection; omitted yields every field.</summary>
    public IList<SelectionSpec> Selections { get; set; } = [];

    /// <summary>The page size; applied after all transformations and selections.</summary>
    public int? PageSize { get; set; }

    /// <summary>The number of rows to skip before the first result.</summary>
    public int? Skip { get; set; }

    /// <summary>An opaque continuation token from a previous response.</summary>
    public string? PageToken { get; set; }

    /// <summary>The request-level default expression language; each slot may override it.</summary>
    public string? Language { get; set; }

    /// <summary>
    ///     The caller the query runs as. Source-level and row-level security read it from here, so it is
    ///     never serialized onto the wire.
    /// </summary>
    /// <remarks>
    ///     The dispatching entry point sets this immediately before handing the request to its handler.
    ///     A request instance therefore belongs to exactly one dispatch: sharing one across concurrent
    ///     calls lets a later caller's principal overwrite an earlier one still in flight, and the query
    ///     would run under the wrong identity. Construct a request per call.
    /// </remarks>
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
