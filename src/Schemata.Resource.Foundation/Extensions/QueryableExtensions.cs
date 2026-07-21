using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;

// ReSharper disable once CheckNamespace
namespace System.Linq;

/// <summary>
///     Extension methods that compose filtering
///     per <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>, ordering, and pagination
///     per <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso> onto query functions.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    ///     Compiles and applies an AIP-160 filter carried by a custom-method request.
    /// </summary>
    /// <remarks>
    ///     Custom method handlers opt into filtering explicitly:
    ///     <code>var filtered = query.ApplyFilter(request, services);</code>
    /// </remarks>
    /// <typeparam name="TEntity">The entity type queried by the custom method.</typeparam>
    /// <param name="query">The query to filter.</param>
    /// <param name="request">The custom-method request carrying the optional filter.</param>
    /// <param name="services">The service provider holding the keyed AIP-160 expression compiler.</param>
    /// <returns>The filtered query, or <paramref name="query" /> when the request has no filter.</returns>
    /// <exception cref="InvalidArgumentException">The filter expression is malformed.</exception>
    public static IQueryable<TEntity> ApplyFilter<TEntity>(
        this IQueryable<TEntity> query,
        IFilterRequest           request,
        IServiceProvider         services
    )
        where TEntity : class {
        if (string.IsNullOrWhiteSpace(request.Filter)) {
            return query;
        }

        try {
            var compiler = services.GetRequiredKeyedService<IExpressionCompiler>(ExpressionLanguages.Aip);
            var tree     = compiler.Parse(request.Filter);
            var filter   = compiler.Compile<TEntity, bool>(tree);

            return query.Where(filter);
        } catch (Exception ex) when (ex is ExpressionException or ArgumentException) {
            throw InvalidFilter();
        }
    }

    private static InvalidArgumentException InvalidFilter() {
        var description = string.Format(SchemataResources.GetResourceString(SchemataResources.INVALID_EXPRESSION), "filter");
        var exception   = new InvalidArgumentException(message: description, reason: SchemataResources.INVALID_FILTER);
        exception.Details!.Add(new BadRequestDetail {
            FieldViolations = [new ErrorFieldViolation {
                Field       = nameof(IFilterRequest.Filter).Underscore(),
                Description = description,
                Reason      = SchemataResources.INVALID_FILTER,
            }],
        });

        return exception;
    }
}
