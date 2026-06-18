using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Parlot;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Expressions.Aip;
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
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
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

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        if (!string.IsNullOrWhiteSpace(request.Parent)) {
            var parent = descriptor.ParseParent(request.Parent);
            if (parent is null) {
                // A supplied parent that does not match the resource's pattern is a client error,
                // not a request to list the top-level collection.
                throw new ValidationException([new() {
                    Field       = nameof(ListRequest.Parent).Underscore(),
                    Description = SchemataResources.GetResourceString(SchemataResources.ST2009),
                    Reason      = FieldReasons.InvalidParent,
                }]);
            }

            if (parent.Any(kv => kv.Value == "-") && !descriptor.SupportsReadAcross) {
                throw new ValidationException([new() {
                    Field       = nameof(ListRequest.Parent).Underscore(),
                    Description = SchemataResources.GetResourceString(SchemataResources.ST2002),
                    Reason      = FieldReasons.CrossParentUnsupported,
                }]);
            }

            var predicate = descriptor.BuildParentPredicate<TEntity>(parent);
            container.ApplyModification(predicate);
        }

        var token = await PageToken.FromStringAsync(request.PageToken, Protector)
                 ?? new PageToken {
                        Parent      = request.Parent,
                        Filter      = request.Filter,
                        OrderBy     = request.OrderBy,
                        ShowDeleted = request.ShowDeleted,
                    };
        if (token.Parent != request.Parent
         || token.Filter != request.Filter
         || token.OrderBy != request.OrderBy
         || token.ShowDeleted != request.ShowDeleted) {
            throw new ValidationException([new() {
                Field       = nameof(request.PageToken).Underscore(),
                Description = SchemataResources.GetResourceString(SchemataResources.ST2003),
                Reason      = FieldReasons.InvalidPageToken,
            }]);
        }

        if (request.PageSize is < 0) {
            throw new ValidationException([new() {
                Field       = nameof(request.PageSize).Underscore(),
                Description = SchemataResources.GetResourceString(SchemataResources.ST2008),
                Reason      = FieldReasons.InvalidPageSize,
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

        if (!string.IsNullOrWhiteSpace(request.Filter)) {
            try {
                var compiler = _sp.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
                var tree     = compiler.Parse(request.Filter);
                var filter   = compiler.Compile<TEntity, bool>(tree);
                container.ApplyFiltering(filter);
            } catch (Exception ex) when (ex is ParseException or ArgumentException) {
                throw new ValidationException([new() {
                    Field = nameof(request.Filter).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "filter"),
                    Reason = FieldReasons.InvalidFilter,
                }]);
            }
        }

        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? order = null;
        if (!string.IsNullOrWhiteSpace(request.OrderBy)) {
            try {
                var compiler = _sp.GetRequiredKeyedService<IOrderCompiler>(AipLanguage.Name);
                order = compiler.CompileOrder<TEntity>(request.OrderBy);
            } catch (Exception ex) when (ex is ParseException or ArgumentException) {
                throw new ValidationException([new() {
                    Field = nameof(request.OrderBy).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST2004), "order_by"),
                    Reason = FieldReasons.InvalidOrderBy,
                }]);
            }
        }

        container.ApplyOrdering(KeyOrdering<TEntity>.Compose(order));

        using var suppression = request.ShowDeleted is true ? _repository.SuppressQuerySoftDelete() : null;

        var totalSize = ResolveTotalSizeMode() switch {
            TotalSizeMode.None => (int?)null,
            TotalSizeMode.Estimated => (int)Math.Min(
                await _repository.EstimateCountAsync(q => container.Query(q), ct.Value), int.MaxValue),
            var _ => await _repository.CountAsync(q => container.Query(q), ct.Value),
        };

        // The extra look-ahead row detects a following page; AIP-158 forbids a
        // next_page_token when the collection is exhausted, and counting cannot be
        // relied on once total_size becomes optional.
        container.ApplyPaginating(token, 1);

        var entities  = _repository.ListAsync(q => container.Query(q), ct.Value);
        var summaries = await _mapper.EachAsync<TEntity, TSummary>(entities, ct.Value).ToListAsync(ct.Value);

        var hasMore = summaries.Count > token.PageSize;
        if (hasMore) {
            summaries.RemoveAt(summaries.Count - 1);
        }

        token.Skip += token.PageSize;

        string? nextPageToken = null;
        if (hasMore) {
            nextPageToken = await token.ToStringAsync(Protector);
        }

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
}
