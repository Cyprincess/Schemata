using System;
using System.Linq.Expressions;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Compiles an AIP-165 purge filter expression. Shared by the dispatch-time request
///     validation and the execute-time replay so both reject the same malformed filters. Purge
///     always pushes the whole filter to the backend; it never degrades to a local residual.
/// </summary>
internal static class PurgeFilter
{
    /// <summary>
    ///     Compiles a purge filter into a predicate over the target resource type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type selected by the purge operation.</typeparam>
    /// <param name="services">The service provider for resolving the expression compiler.</param>
    /// <param name="filter">The filter expression, or <c>*</c> for every soft-deleted resource.</param>
    /// <param name="language">The filter language, or null for the resource's default.</param>
    /// <returns>The compiled predicate, or <see langword="null" /> when the filter selects every resource.</returns>
    /// <exception cref="ValidationException">The filter or language is missing or malformed.</exception>
    public static Expression<Func<TEntity, bool>>? Compile<TEntity>(
        IServiceProvider services,
        string?          filter,
        string?          language
    )
        where TEntity : class {
        if (string.IsNullOrWhiteSpace(filter)) {
            throw InvalidFilter();
        }

        if (filter == Wildcards.Any) {
            return null;
        }

        var profile = services.GetService<IOptions<SchemataResourceOptions>>()?.Value.Expressions
                   ?? new ExpressionLanguageProfile();

        string resolved;
        try {
            resolved = ExpressionLanguageResolver.Resolve(profile, language,
                                                          services.GetKeyedService<ExpressionLanguageDescriptor>)
                                                 .Language;
        } catch (UnknownExpressionLanguageException) {
            throw InvalidLanguage();
        }

        try {
            var compiler = services.GetRequiredKeyedService<IExpressionCompiler>(resolved);
            var tree     = compiler.Parse(filter);
            return compiler.Compile<TEntity, bool>(tree);
        } catch (Exception ex) when (ex is ExpressionException or ArgumentException) {
            throw InvalidFilter();
        }
    }

    private static ValidationException InvalidFilter() {
        return new([new() {
            Field       = nameof(PurgeRequest.Filter).Underscore(),
            Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "filter"),
            Reason      = FieldReasons.InvalidFilter,
        }]);
    }

    private static ValidationException InvalidLanguage() {
        return new([new() {
            Field       = nameof(PurgeRequest.Language).Underscore(),
            Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "language"),
            Reason      = FieldReasons.InvalidFilter,
        }]);
    }
}
