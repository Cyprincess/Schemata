using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail containing request identification information for debugging.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class RequestInfoDetail : IErrorDetail
{
    /// <summary>
    ///     Gets or sets the unique request identifier (trace identifier).
    /// </summary>
    public virtual string? RequestId { get; set; }

    /// <summary>
    ///     Gets or sets opaque serving data for diagnostics.
    /// </summary>
    public virtual string? ServingData { get; set; }

    #region IErrorDetail Members

    /// <inheritdoc />
    public string Type => "type.googleapis.com/google.rpc.RequestInfo";

    #endregion
}
