using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;

namespace Schemata.Insight.Foundation.Drivers;

/// <summary>
///     Routes Insight source execution to the repository source registered under the source's
///     binding. All source-specific behavior (entitlement, projection, filters, ordering, residual)
///     lives on the closed generic <c>RepositorySource&lt;TEntity,TPublic&gt;</c> instance; the
///     driver only resolves the binding and delegates.
/// </summary>
public sealed class RepositoryDriver(IServiceProvider services) : ISourceDriver
{
    /// <summary>The keyed name under which this driver registers and sources reference it.</summary>
    public const string DriverName = "repository";

    /// <inheritdoc />
    public string Name => DriverName;

    /// <inheritdoc />
    public DriverCapabilities Capabilities
        => DriverCapabilities.Filter
         | DriverCapabilities.Project
         | DriverCapabilities.Order
         | DriverCapabilities.Nested;

    /// <inheritdoc />
    public ValueTask<ISourceResult> ExecuteAsync(
        SubPlan             subPlan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct = default
    ) {
        var source = Resolve(services, subPlan.Config);
        return source.ExecuteAsync(services, subPlan, request, principal, ct);
    }

    /// <summary>
    ///     Validates the source parameters carry a binding name and resolves the registered
    ///     <see cref="RepositorySource" /> for it.
    /// </summary>
    /// <param name="services">The service provider that owns the keyed repository source registrations.</param>
    /// <param name="config">The source configuration to resolve.</param>
    /// <returns>The repository source bound to the source's configured binding name.</returns>
    /// <exception cref="InsightValidationException">
    ///     The configuration is missing the binding parameter, or no <see cref="RepositorySource" /> is
    ///     registered under that name.
    /// </exception>
    internal static RepositorySource Resolve(IServiceProvider services, SourceConfig config) {
        if (!config.Params.TryGetValue("binding", out var value) || value is not string binding) {
            throw new InsightValidationException(
                InsightReasons.InvalidArgument,
                SchemataResources.INSIGHT_SOURCE_REQUIRES_RESOURCE);
        }

        var source = services.GetKeyedService<RepositorySource>(binding);
        if (source is null) {
            throw new InsightValidationException(
                InsightReasons.UnknownSourceName,
                SchemataResources.INSIGHT_UNKNOWN_SOURCE,
                new Dictionary<string, string?> { ["name"] = binding });
        }

        return source;
    }
}
