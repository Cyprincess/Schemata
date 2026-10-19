using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Extensions;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Models;
using Schemata.Transport.Http.Linking;

namespace Schemata.Insight.Http.Linking;

/// <summary>
///     Contributes the query result's self, next and previous links. The query is body-bound, so each
///     navigation link carries its continuation token as a target attribute. The response's
///     <see cref="QueryInsightResponse.NextPageToken" /> drives the next link; the request's page
///     token drives the previous link, so every page after the first keeps a previous link on the
///     final page.
/// </summary>
public sealed class InsightLinkContributor(IDataProtectionProvider protection) : ILinkContributor
{
    private IDataProtector Protector => field ??= protection.CreateProtector(InsightPageToken.ProtectionPurpose);

    #region ILinkContributor Members

    public IEnumerable<WebLink> GetLinks(LinkContext context) {
        if (context.Value is not QueryInsightResponse response) {
            yield break;
        }

        var target = UriHelper.GetEncodedPathAndQuery(context.HttpContext.Request);
        yield return new(target, "self");

        if (!string.IsNullOrWhiteSpace(response.NextPageToken)) {
            yield return new(target, "next", Extensions: Token(response.NextPageToken!));
        }

        if (InsightQueryState.GetPageToken(context.HttpContext) is { Length: > 0 } current
         && Previous(current) is { } previous) {
            yield return new(target, "prev", Extensions: Token(previous));
        }
    }

    #endregion

    private static IReadOnlyDictionary<string, string> Token(string token) => new Dictionary<string, string> {
        ["page_token"] = token,
    };

    private string? Previous(string current) {
        try {
            return InsightPageToken.EncodePrevious(Protector, current);
        } catch (Exception error) when (error is FormatException
                                     or CryptographicException
                                     or JsonException
                                     or IOException
                                     or InsightValidationException) {
            // Navigation links are ancillary response metadata: an unusable continuation must not
            // turn a successful query result into a failure.
            return null;
        }
    }
}
