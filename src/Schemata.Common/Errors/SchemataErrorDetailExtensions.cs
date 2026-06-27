using System;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Common.Errors;

/// <summary>
///     Fluent helpers that attach optional <see cref="IErrorDetail" /> entries to an
///     in-flight <see cref="SchemataException" /> so throw sites can decorate the response
///     without overloading every constructor with extra parameters.
/// </summary>
/// <remarks>
///     Per <seealso href="https://google.aip.dev/193">AIP-193 §Help</seealso>, supplemental
///     documentation links live in <see cref="HelpDetail" />, and per the spec's
///     <c>RetryInfo</c> guidance, retry-eligible failures (<c>RESOURCE_EXHAUSTED</c>,
///     <c>UNAVAILABLE</c>, sometimes <c>ABORTED</c>) should advertise a recommended
///     backoff via <see cref="RetryInfoDetail" />.
/// </remarks>
public static class SchemataErrorDetailExtensions
{
    /// <summary>
    ///     Attaches (or replaces) a <see cref="RetryInfoDetail" /> with the recommended
    ///     wait interval.
    /// </summary>
    /// <typeparam name="TException">The concrete exception type.</typeparam>
    /// <param name="exception">The exception to decorate.</param>
    /// <param name="retryAfter">Suggested delay before the caller retries.</param>
    /// <returns>The same exception instance for fluent chaining.</returns>
    public static TException WithRetryAfter<TException>(this TException exception, TimeSpan retryAfter)
        where TException : SchemataException {
        exception.Details ??= [];
        RemoveExisting<RetryInfoDetail>(exception);
        exception.Details.Add(new RetryInfoDetail { RetryDelay = retryAfter });
        return exception;
    }

    /// <summary>
    ///     Attaches a single <see cref="ErrorHelpLink" /> to the exception's
    ///     <see cref="HelpDetail" />, creating the detail when absent and appending the
    ///     link when present.
    /// </summary>
    /// <typeparam name="TException">The concrete exception type.</typeparam>
    /// <param name="exception">The exception to decorate.</param>
    /// <param name="description">
    ///     Plain-text description shown as the hyperlink label; must be non-empty and
    ///     contain no HTML / Markdown markup per AIP-193 §Help.
    /// </param>
    /// <param name="url">Absolute URL (including scheme) of the linked documentation.</param>
    /// <returns>The same exception instance for fluent chaining.</returns>
    public static TException WithHelp<TException>(this TException exception, string description, string url)
        where TException : SchemataException {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        exception.Details ??= [];
        var help = FindOrAdd<HelpDetail>(exception, static () => new());
        help.Links ??= [];
        help.Links.Add(new() { Description = description, Url = url });
        return exception;
    }

    private static void RemoveExisting<TDetail>(SchemataException exception) where TDetail : class, IErrorDetail {
        if (exception.Details is null) {
            return;
        }

        for (var i = exception.Details.Count - 1; i >= 0; i--) {
            if (exception.Details[i] is TDetail) {
                exception.Details.RemoveAt(i);
            }
        }
    }

    private static TDetail FindOrAdd<TDetail>(SchemataException exception, Func<TDetail> factory)
        where TDetail : class, IErrorDetail {
        foreach (var detail in exception.Details!) {
            if (detail is TDetail existing) {
                return existing;
            }
        }

        var created = factory();
        exception.Details.Add(created);
        return created;
    }
}
