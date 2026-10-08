using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton;
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
        return UpdateCoreAsync(ctx, name, request, principal, ct.Value);
    }

    /// <summary>
    ///     Runs update processing with an existing advisor context.
    /// </summary>
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="request">The update request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <see cref="UpdateResultBase{TDetail}" /> containing the updated detail DTO.</returns>
    internal async Task<UpdateResultBase<TDetail>> UpdateCoreAsync(
        AdviceContext     ctx,
        string            name,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        ResourceNameDescriptor.ForType<TEntity>().ClearParentProperties(request);

        var container = new ResourceRequestContainer<TEntity>();
        ResourceIdentifiers.Apply(container, name);

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

        if (entity is null) {
            // Entitlement-filtered null and physical absence are indistinguishable; authorize
            // the missing outcome before create-on-missing (whose create-side permissions still
            // run) or a NOT_FOUND — an unauthorized caller learns neither.
            if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Update))
             && _sp.GetKeyedService<ResourceAccessStage>(typeof(TEntity)) is { } access) {
                await access.FinalizeMissingAsync<TEntity, TRequest>(nameof(Operations.Update), request, name, principal, ct);
            }

            if (request is IAllowMissing { AllowMissing: true }) {
                return await CreateMissingAsync(ctx, name, request, principal, ct);
            }

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
        ResourceSanitizePipelineAdvisor.RetainSystemFields(
            entity,
            ResourceSanitizePipelineAdvisor.UpdateSystemFields,
            () => {
                if (mask is null || mask.Trim() == Wildcards.Any) {
                    _mapper.Map(request, entity);
                } else {
                    _mapper.Map(request, entity, ResolveMaskFields(mask));
                }
            });

        var mutation = _sp.GetRequiredService<IResourceMutation<TEntity>>();
        await mutation.UpdateAsync(entity, null, Operations.Update, ct);

        var detail = RequireDetail(_mapper.Map<TEntity, TDetail>(entity));

        return new() { Detail = detail };
    }

    private async Task<UpdateResultBase<TDetail>> CreateMissingAsync(
        AdviceContext     ctx,
        string            name,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var container = new ResourceRequestContainer<TEntity>();
        ResourceIdentifiers.Apply(container, name);
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        if (descriptor.HasParent && descriptor.ParseCanonicalName(name) is { } addressed) {
            var routeValues = new Dictionary<string, object?>(addressed.ParentValues.Count);
            foreach (var parent in addressed.ParentValues) routeValues[parent.Key] = parent.Value;
            descriptor.SetParentFromRouteValues(request, routeValues);
        }

        var requestResult = await RunPipelineAsync<UpdateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest>>()
                         .RunAsync(ctx, request, container, principal, ct), CollectionNotFound);
        if (requestResult is not null) {
            return requestResult;
        }

        var entity = _mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            throw new ValidationException([new() {
                Field       = nameof(request),
                Description = SchemataResources.GetResourceString(SchemataResources.INVALID_PAYLOAD),
                Reason      = SchemataResources.INVALID_PAYLOAD,
            }]);
        }
        AdviceApplyChildParent<TEntity, TRequest>.Apply(request, entity);

        var entityResult = await RunPipelineAsync<UpdateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceCreateAdvisor<TEntity, TRequest>>()
                         .RunAsync(ctx, request, entity, principal, ct), CollectionNotFound);
        if (entityResult is not null) {
            return entityResult;
        }

        var mutation = _sp.GetRequiredService<IResourceMutation<TEntity>>();
        await mutation.CreateAsync(entity, null, ct);

        var detail = RequireDetail(_mapper.Map<TEntity, TDetail>(entity));

        return new() { Detail = detail };
    }

    private static List<string> ResolveMaskFields(string mask) {
        try {
            return MaskTree.FromWire(typeof(TEntity), mask, false, ResourceWireNameRules.ResolveClrName).LeafPaths().ToList();
        } catch (ArgumentException ex) {
            throw InvalidUpdateMaskPath(mask, ex.Message);
        }
    }

    private static ValidationException InvalidUpdateMaskPath(string path, string reason) {
        return new([
            new() {
                Field       = nameof(IUpdateMask.UpdateMask).Underscore(),
                Description = LocalizedMessageFormatter.FormatInvariant(
                    SchemataResources.INVALID_UPDATE_MASK,
                    new Dictionary<string, string?> { ["path"] = path, ["reason"] = reason }),
                Reason      = SchemataResources.INVALID_UPDATE_MASK,
            },
        ]);
    }
}
