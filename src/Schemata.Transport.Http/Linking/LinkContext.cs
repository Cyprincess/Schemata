using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Core.Building;

namespace Schemata.Transport.Http.Linking;

/// <summary>
///     The explicit request and result state available to a <see cref="ILinkContributor" />.
/// </summary>
/// <param name="HttpContext">The current request.</param>
/// <param name="Result">The action result being shaped, or <see langword="null" />.</param>
/// <param name="Resource">The resource registration the result belongs to, or <see langword="null" />.</param>
public sealed record LinkContext(
    HttpContext HttpContext,
    object? Result,
    ResourceRegistration? Resource)
{
    /// <summary>
    ///     The payload carried by <see cref="Result" /> when MVC wraps it in a JSON-bearing result.
    /// </summary>
    public object? Value => Result switch {
        JsonResult json  => json.Value,
        ObjectResult obj => obj.Value,
        _                => null,
    };
}
