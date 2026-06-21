using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

public sealed partial class ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     Updates a resource
    ///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso> through the full advisor
    ///     pipeline.
    ///     Authorization is checked before the entity is loaded
    ///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso>.
    ///     Uses field masks
    ///     per <seealso href="https://google.aip.dev/161">AIP-161: Field masks</seealso> when the request implements
    ///     <see cref="IUpdateMask" />.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="request">The update request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="UpdateResultBase{TDetail}" /> containing the updated detail DTO.</returns>
    public Task<UpdateResultBase<TDetail>> UpdateAsync(
        string             name,
        TRequest           request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;
        var ctx = CreateAdviceContext();
        return UpdateCoreAsync(ctx, name, request, principal, ct.Value, true);
    }

    /// <summary>
    ///     Runs update processing with an existing advisor context.
    /// </summary>
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="request">The update request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="finalize">Whether to commit the repository and run response advisors.</param>
    /// <returns>An <see cref="UpdateResultBase{TDetail}" /> containing the updated detail DTO.</returns>
    internal async Task<UpdateResultBase<TDetail>> UpdateCoreAsync(
        AdviceContext     ctx,
        string            name,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken ct,
        bool              finalize
    ) {
        var gate = await RunPipelineAsync<UpdateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceRequestAdvisor<TEntity>>()
                         .RunAsync(ctx, principal, nameof(Operations.Update), ct), () => ResourceNotFound(name));
        if (gate is not null) {
            return gate;
        }

        ResourceNameDescriptor.ForType<TEntity>().ClearParentProperties(request);

        // The URI identifies the resource being updated; carry it on the request so the AIP-155
        // idempotency key distinguishes updates to different resources that share a request id.
        request.CanonicalName = name;

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        var requestResult = await RunPipelineAsync<UpdateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceUpdateRequestAdvisor<TEntity, TRequest>>()
                         .RunAsync(ctx, request, container, principal, ct), () => ResourceNotFound(name));
        if (requestResult is not null) {
            return requestResult;
        }

        TEntity? entity;
        using (_repository.SuppressQuerySoftDelete()) {
            entity = await _repository.SingleOrDefaultAsync(q => container.Query(q), ct);
        }

        if (entity == null) {
            throw ResourceNotFound(name);
        }

        var entityResult = await RunPipelineAsync<UpdateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceUpdateAdvisor<TEntity, TRequest>>()
                         .RunAsync(ctx, request, entity, principal, ct), () => ResourceNotFound(name));
        if (entityResult is not null) {
            return entityResult;
        }

        var mask = (request as IUpdateMask)?.UpdateMask;
        if (mask is null || mask.Trim() == Wildcards.Any) {
            _mapper.Map(request, entity);
        } else {
            _mapper.Map(request, entity, ResolveMaskFields(mask));
        }

        await _repository.UpdateAsync(entity, ct);

        if (!finalize) {
            var staged = _mapper.Map<TEntity, TDetail>(entity);
            return new() { Detail = staged };
        }

        await _repository.CommitAsync(ct);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        var responseResult = await RunPipelineAsync<UpdateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                         .RunAsync(ctx, entity, detail, principal, ct), () => ResourceNotFound(name));
        return responseResult ?? new() { Detail = detail };
    }

    private static List<string> ResolveMaskFields(string mask) {
        try {
            return MaskTree.FromWire(typeof(TEntity), mask, false, ResourceWireMask.Convert).LeafPaths().ToList();
        } catch (ArgumentException ex) {
            throw InvalidUpdateMaskPath(mask, ex.Message);
        }
    }

    private static ValidationException InvalidUpdateMaskPath(string path, string reason) {
        return new([
            new() {
                Field       = nameof(IUpdateMask.UpdateMask).Underscore(),
                Description = $"The update_mask path `{path}` is invalid: {reason}.",
                Reason      = FieldReasons.InvalidUpdateMask,
            },
        ]);
    }
}
