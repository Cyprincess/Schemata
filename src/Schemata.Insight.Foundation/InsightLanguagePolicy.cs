using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation.Planning;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Resolves the module's expression-language policy: admission through the enabled-language
///     profile, and the residual scan caps that bound local execution work.
/// </summary>
internal static class InsightLanguagePolicy
{
    internal const int DefaultScanRows = 10_000;

    /// <summary>
    ///     Resolves the admission contract for a requested language: it must be enabled in the module
    ///     profile, and the returned filtering mode and scan cap are narrowed by the module,
    ///     per-language, and language-default levels.
    /// </summary>
    internal static ResolvedLanguage Require(SchemataInsightOptions options, IServiceProvider services, string? requested) {
        try {
            return ExpressionLanguageResolver.Resolve(options.Expressions, requested, services.GetKeyedService<ExpressionLanguageDescriptor>);
        } catch (UnknownExpressionLanguageException ex) {
            throw new InsightValidationException(
                InsightReasons.UnknownExpressionLanguage,
                SchemataResources.INSIGHT_EXPRESSION_LANGUAGE_UNKNOWN,
                new Dictionary<string, string?> { ["language"] = ex.Requested ?? string.Empty }, ex);
        }
    }

    /// <summary>
    ///     The scan cap for local work evaluating <paramref name="language" /> expressions: its
    ///     resolved per-language cap, or the module cap when the profile does not list the language
    ///     (a plan built directly rather than through admission).
    /// </summary>
    internal static int ScanCap(SchemataInsightOptions options, IServiceProvider services, string? language) {
        if (!string.IsNullOrWhiteSpace(language)) {
            foreach (var entry in options.Expressions.Languages) {
                if (string.Equals(entry.Language, language, StringComparison.Ordinal)) {
                    return Require(options, services, language).MaxResidualScanRows;
                }
            }
        }

        return ModuleCap(options, services);
    }

    /// <summary>
    ///     The strictest scan cap over the languages a pipeline evaluates locally; language-less
    ///     stages use the module cap.
    /// </summary>
    internal static int StrictestCap(SchemataInsightOptions options, IServiceProvider services, IEnumerable<string> languages) {
        var cap = int.MaxValue;
        var any = false;
        foreach (var language in languages) {
            cap = Math.Min(cap, ScanCap(options, services, language));
            any = true;
        }

        return any ? cap : ModuleCap(options, services);
    }

    private static int ModuleCap(SchemataInsightOptions options, IServiceProvider services) {
        if (options.Expressions.Languages.Count == 0) {
            return options.Expressions.MaxResidualScanRows > 0 ? options.Expressions.MaxResidualScanRows : DefaultScanRows;
        }

        return Require(options, services, null).MaxResidualScanRows;
    }
}
