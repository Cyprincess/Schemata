using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail carrying request-identification information to assist with debugging
///     and log correlation, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class RequestInfoDetail : IErrorDetail
{
    /// <summary>
    ///     Trace identifier for the request, used to correlate server-side logs with
    ///     client reports.
    /// </summary>
    public virtual string? RequestId { get; set; }

    /// <summary>
    ///     Opaque data captured at request time for diagnostics
    ///     (e.g. serialized headers, routing annotations).
    /// </summary>
    public virtual string? ServingData { get; set; }

    #region IErrorDetail Members

    /// <summary>
    ///     Returns <c>"type.googleapis.com/google.rpc.RequestInfo"</c>.
    /// </summary>
    public string Type => "type.googleapis.com/google.rpc.RequestInfo";

    #endregion
}
