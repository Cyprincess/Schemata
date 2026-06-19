using System;
using System.Linq.Expressions;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Parlot;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Compiles an AIP-165 purge filter expression. Shared by the dispatch-time request
///     validation and the execute-time replay so both reject the same malformed filters.
/// </summary>
internal static class PurgeFilter
{
    /// <summary>
    ///     Compiles a purge filter into a predicate over the target resource type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type selected by the purge operation.</typeparam>
    /// <param name="services">The service provider for resolving the AIP expression compiler.</param>
    /// <param name="filter">The AIP filter expression, or <c>*</c> for every soft-deleted resource.</param>
    /// <returns>The compiled predicate, or <see langword="null" /> when the filter selects every resource.</returns>
    /// <exception cref="ValidationException">The filter is missing or malformed.</exception>
    public static Expression<Func<TEntity, bool>>? Compile<TEntity>(IServiceProvider services, string? filter)
        where TEntity : class {
        if (string.IsNullOrWhiteSpace(filter)) {
            throw InvalidFilter();
        }

        if (filter == Wildcards.Any) {
            return null;
        }

        try {
            var compiler = services.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
            var tree     = compiler.Parse(filter);
            return compiler.Compile<TEntity, bool>(tree);
        } catch (Exception ex) when (ex is ParseException or ArgumentException) {
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
}
