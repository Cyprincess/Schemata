using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail carrying a reason code, domain, and arbitrary metadata for structured
///     error classification, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class ErrorInfoDetail : IErrorDetail
{
    /// <summary>
    ///     Machine-readable reason code for client-side branching
    ///     (e.g. <c>"RESOURCE_NOT_FOUND"</c>, <c>"CONCURRENCY_MISMATCH"</c>).
    /// </summary>
    public virtual string? Reason { get; set; }

    /// <summary>
    ///     Logical service domain that produced the error (e.g. <c>"schemata.io"</c>).
    /// </summary>
    public virtual string? Domain { get; set; }

    /// <summary>
    ///     Arbitrary key-value pairs providing additional diagnostic context.
    /// </summary>
    public virtual Dictionary<string, string>? Metadata { get; set; }

    #region IErrorDetail Members

    /// <summary>
    ///     Returns <c>"type.googleapis.com/google.rpc.ErrorInfo"</c>.
    /// </summary>
    public string Type => "type.googleapis.com/google.rpc.ErrorInfo";

    #endregion
}
