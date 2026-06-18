using System;
using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail describing how long the caller should wait before retrying the
///     request, per <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
/// <remarks>
///     An extension detail payload: the framework defines and serializes the shape, but the
///     application layer decides when to attach it to an error. The framework core never
///     populates it on its own.
/// </remarks>
[Polymorphic(typeof(IErrorDetail), Name = "type.googleapis.com/google.rpc.RetryInfo")]
public class RetryInfoDetail : IErrorDetail
{
    /// <summary>
    ///     Recommended interval to wait before retrying. <see langword="null" /> indicates
    ///     no retry guidance is provided.
    /// </summary>
    public virtual TimeSpan? RetryDelay { get; set; }
}
