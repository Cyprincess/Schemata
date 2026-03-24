using System.Collections.Generic;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail providing structured information about an error, including the reason and domain.
/// </summary>
[Polymorphic(typeof(IErrorDetail))]
public class ErrorInfoDetail : IErrorDetail
{
    /// <summary>
    ///     Gets or sets the machine-readable reason code.
    /// </summary>
    public virtual string? Reason { get; set; }

    /// <summary>
    ///     Gets or sets the logical domain of the error (e.g., "schemata.io").
    /// </summary>
    public virtual string? Domain { get; set; }

    /// <summary>
    ///     Gets or sets additional key-value metadata about the error.
    /// </summary>
    public virtual Dictionary<string, string>? Metadata { get; set; }

    #region IErrorDetail Members

    /// <inheritdoc />
    public string Type => "type.googleapis.com/google.rpc.ErrorInfo";

    #endregion
}
