using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Expressions.Skeleton;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

public sealed partial class ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     Lists resources with filtering
    ///     per <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>, ordering, and pagination
    ///     per <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso> through the full advisor pipeline.
    /// </summary>
    /// <param name="request">The list request with filter, order, paging, and parent parameters.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ListResultBase{TSummary}" /> with summaries and an optional next page token.</returns>
    public async Task<ListResultBase<TSummary>> ListAsync(
        ListRequest        request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();
        StashReadMask(ctx, request.ReadMask);

        var gate = await RunPipelineAsync<ListResultBase<TSummary>>(
            ctx,
            () => Advisor.For<IResourceRequestAdvisor<TEntity>>()
                         .RunAsync(ctx, principal, nameof(Operations.List), ct.Value), CollectionNotFound);
        if (gate is not null) {
            return gate;
        }

        var container = new ResourceRequestContainer<TEntity>();

        var requestResult = await RunPipelineAsync<ListResultBase<TSummary>>(
            ctx,
            () => Advisor.For<IResourceListRequestAdvisor<TEntity>>()
                         .RunAsync(ctx, request, container, principal, ct.Value), CollectionNotFound);
        if (requestResult is not null) {
            return requestResult;
        }

        ResourceIdentifiers.ApplyParent(container, request.Parent);

        var token = await PageToken.FromStringAsync(request.PageToken, Protector)
                 ?? new PageToken {
                        Parent      = request.Parent,
                        Filter      = request.Filter,
                        Language    = request.Language,
                        OrderBy     = request.OrderBy,
                        ShowDeleted = request.ShowDeleted,
                    };
        if (token.Parent != request.Parent
         || token.Filter != request.Filter
         || token.Language != request.Language
         || token.OrderBy != request.OrderBy
         || token.ShowDeleted != request.ShowDeleted) {
            throw new ValidationException([new() {
                Field       = nameof(request.PageToken).Underscore(),
                Description = SchemataResources.GetResourceString(SchemataResources.INVALID_PAGE_TOKEN),
                Reason      = SchemataResources.INVALID_PAGE_TOKEN,
            }]);
        }

        if (request.PageSize is < 0) {
            throw new ValidationException([new() {
                Field       = nameof(request.PageSize).Underscore(),
                Description = SchemataResources.GetResourceString(SchemataResources.INVALID_PAGE_SIZE),
                Reason      = SchemataResources.INVALID_PAGE_SIZE,
            }]);
        }

        if (request.PageSize.HasValue) {
            token.PageSize = request.PageSize.Value;
        }

        token.PageSize = token.PageSize switch {
            <= 0  => 25,
            > 100 => 100,
            var _ => token.PageSize,
        };

        if (request.Skip.HasValue) {
            token.Skip += request.Skip.Value;
        }

        if (token.Skip < 0) {
            token.Skip = 0;
        }

        Func<TEntity, bool>? residual = null;
        ResolvedLanguage?    resolved = null;
        if (!string.IsNullOrWhiteSpace(request.Filter)) {
            resolved = ResolveFilterLanguage(request.Language);
            var compiler = _sp.GetRequiredKeyedService<IExpressionCompiler>(resolved.Language);
            try {
                var tree = compiler.Parse(request.Filter);
                if (resolved.Filtering is FilteringMode.Residual) {
                    var planner = _sp.GetRequiredKeyedService<IExpressionPushdownPlanner>(resolved.Language);
                    var plan    = planner.Plan(tree, ExpressionCapabilities.Relational);
                    if (plan.Pushed is not null) {
                        container.ApplyFiltering(compiler.Compile<TEntity, bool>(plan.Pushed));
                    }

                    if (plan.Residual is not null) {
                        residual = compiler.Compile<TEntity, bool>(plan.Residual).Compile();
                    }
                } else {
                    container.ApplyFiltering(compiler.Compile<TEntity, bool>(tree));
                }
            } catch (Exception ex) when (ex is ExpressionException or ArgumentException) {
                throw new ValidationException([new() {
                    Field = nameof(request.Filter).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.INVALID_EXPRESSION), "filter"),
                    Reason = SchemataResources.INVALID_FILTER,
                }]);
            }
        }

        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? order = null;
        if (!string.IsNullOrWhiteSpace(request.OrderBy)) {
            try {
                var compiler = _sp.GetRequiredService<IOrderCompiler>();
                order = compiler.CompileOrder<TEntity>(request.OrderBy);
            } catch (ArgumentException) {
                throw new ValidationException([new() {
                    Field = nameof(request.OrderBy).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.INVALID_EXPRESSION), "order_by"),
                    Reason = SchemataResources.INVALID_ORDER_BY,
                }]);
            }
        }

        container.ApplyOrdering(KeyOrdering<TEntity>.Compose(order));

        using var suppression = request.ShowDeleted is true ? _repository.SuppressQuerySoftDelete() : null;

        List<TSummary> summaries;
        bool           hasMore;
        int?           totalSize;

        if (residual is null) {
            totalSize = ResolveTotalSizeMode() switch {
                TotalSizeMode.None => null,
                TotalSizeMode.Estimated => (int)Math.Min(
                    await _repository.EstimateCountAsync(q => container.Query(q), ct.Value), int.MaxValue),
                var _ => await _repository.CountAsync(q => container.Query(q), ct.Value),
            };

            // The extra look-ahead row detects a following page; AIP-158 forbids a
            // next_page_token when the collection is exhausted, and counting cannot be
            // relied on once total_size becomes optional.
            container.ApplyPaginating(token, 1);

            var entities = _repository.ListAsync(q => container.Query(q), ct.Value);
            summaries = await _mapper.EachAsync<TEntity, TSummary>(entities, ct.Value).ToListAsync(ct.Value);

            hasMore = summaries.Count > token.PageSize;
            if (hasMore) {
                summaries.RemoveAt(summaries.Count - 1);
            }
        } else {
            var mode = ResolveTotalSizeMode();

            // The residual runs locally over the pushed superset, so paging and an exact total are
            // computed after the residual rather than in the backend query.
            var superset = _repository.ListAsync(q => container.Query(q), ct.Value);
            var scan = await ResidualPage.ScanAsync(
                superset, residual, token.Skip, token.PageSize, resolved!.MaxResidualScanRows,
                mode is TotalSizeMode.Exact, ct.Value);

            totalSize = mode switch {
                TotalSizeMode.None => null,
                TotalSizeMode.Estimated => (int)Math.Min(
                    await _repository.EstimateCountAsync(q => container.Query(q), ct.Value), int.MaxValue),
                var _ => scan.Total,
            };

            summaries = new(scan.Page.Count);
            foreach (var entity in scan.Page) {
                var summary = _mapper.Map<TEntity, TSummary>(entity);
                if (summary is not null) {
                    summaries.Add(summary);
                }
            }

            hasMore = scan.HasMore;
        }

        token.Skip += token.PageSize;

        string? nextPageToken = hasMore ? await token.ToStringAsync(Protector) : null;

        var immutable = summaries.ToImmutableArray();

        var responseResult = await RunPipelineAsync<ListResultBase<TSummary>>(
            ctx,
            () => Advisor.For<IResourceListResponseAdvisor<TSummary>>().RunAsync(ctx, immutable, principal, ct.Value),
            CollectionNotFound);
        if (responseResult is not null) {
            return responseResult;
        }

        return new() {
            TotalSize = totalSize, Entities = immutable, NextPageToken = nextPageToken,
        };
    }

    private ResolvedLanguage ResolveFilterLanguage(string? requested) {
        var profile = _sp.GetService<IOptions<SchemataResourceOptions>>()?.Value.Expressions
                   ?? new ExpressionLanguageProfile();
        try {
            return ExpressionLanguageResolver.Resolve(profile, requested,
                                                      n => _sp.GetKeyedService<ExpressionLanguageDescriptor>(n));
        } catch (UnknownExpressionLanguageException) {
            throw new ValidationException([new() {
                Field       = nameof(ListRequest.Language).Underscore(),
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.INVALID_EXPRESSION), "language"),
                Reason      = SchemataResources.INVALID_FILTER,
            }]);
        }
    }
}
