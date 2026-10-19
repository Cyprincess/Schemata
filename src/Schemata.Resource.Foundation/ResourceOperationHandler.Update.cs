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
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="execution">
    ///     The identified dispatch execution carried from the idempotency wrap, or
    ///     <see langword="null" /> when the dispatch runs unidentified. Non-null dispatches commit
    ///     through this handler so the wrap receives transaction-completion evidence.
    /// </param>
    /// <returns>An <see cref="UpdateResultBase{TDetail}" /> containing the updated detail DTO.</returns>
    public Task<UpdateResultBase<TDetail>> UpdateAsync(
        string                       name,
        TRequest                     request,
        ClaimsPrincipal?             principal,
        AdviceContext                ctx,
        CancellationToken?           ct,
        ResourceDispatchExecution?   execution = null
    ) {
        ct ??= CancellationToken.None;
        return UpdateCoreAsync(ctx, name, request, principal, ct.Value, execution);
    }

    /// <summary>
    ///     Runs update processing with an existing advisor context.
    /// </summary>
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="request">The update request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="execution">The identified dispatch execution, or <see langword="null" />.</param>
    /// <returns>An <see cref="UpdateResultBase{TDetail}" /> containing the updated detail DTO.</returns>
    internal async Task<UpdateResultBase<TDetail>> UpdateCoreAsync(
        AdviceContext                 ctx,
        string                        name,
        TRequest                      request,
        ClaimsPrincipal?              principal,
        CancellationToken             ct,
        ResourceDispatchExecution?    execution = null
    ) {
        ResourceNameDescriptor.ForType<TEntity>().ClearParentProperties(request);

        var container = new ResourceRequestContainer<TEntity>();
        ResourceIdentifiers.Apply(container, name);

        var requestOutput = new HandledResponse<UpdateResultBase<TDetail>>();
        var requestResult = await HandledResponseRunner.RunAsync(
            requestOutput,
            () => Advisor.For<IResourceUpdateRequestAdvisor<TEntity, TRequest, TDetail>>()
                         .RunAsync(ctx, request, container, principal, requestOutput, ct), () => ResourceNotFound(name));
        if (requestResult is not null) {
            return requestResult;
        }

        var entity = await _repository.SingleOrDefaultAsync(
            q => container.Query(q), ct, new QueryOptions(IgnoreSoftDelete: true));

        if (entity is null) {
            // Entitlement-filtered null and physical absence are indistinguishable; authorize
            // the missing outcome before create-on-missing (whose create-side permissions still
            // run) or a NOT_FOUND — an unauthorized caller learns neither.
            if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Update))
             && _sp.GetKeyedService<ResourceAccessStage>(typeof(TEntity)) is { } access) {
                await access.FinalizeMissingAsync<TEntity, TRequest>(nameof(Operations.Update), request, name, principal, ct);
            }

            if (request is IAllowMissing { AllowMissing: true }) {
                return await CreateMissingAsync(ctx, name, request, principal, execution, ct);
            }

            throw ResourceNotFound(name);
        }

        var entityOutput = new HandledResponse<UpdateResultBase<TDetail>>();
        var entityResult = await HandledResponseRunner.RunAsync(
            entityOutput,
            () => Advisor.For<IResourceUpdateAdvisor<TEntity, TRequest, TDetail>>()
                         .RunAsync(ctx, request, entity, principal, entityOutput, ct), () => ResourceNotFound(name));
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
        if (execution is null) {
            var outcome = await mutation.UpdateAsync(entity, null, Operations.Update, ct);

            // NoWrite means no change was staged: the mapped in-memory values were not persisted
            // and must not be reported as the updated resource.
            if (outcome == MutationResult.NoWrite) {
                return new();
            }

            return new() { Detail = RequireDetail(_mapper.Map<TEntity, TDetail>(entity)) };
        }

        // Identified dispatch: this handler owns the commit so the wrap receives transaction
        // completion through the execution payload, and the fail-able detail mapping runs before
        // the provider commit.
        return await ExecuteOwnedAsync(
            execution,
            transaction => MutateUpdateAsync(mutation, entity, transaction, execution, ct),
            ct);
    }

    private async Task<UpdateResultBase<TDetail>> MutateUpdateAsync(
        IResourceMutation<TEntity> mutation,
        TEntity                    entity,
        IUnitOfWork                transaction,
        ResourceDispatchExecution  execution,
        CancellationToken          ct
    ) {
        var outcome = await mutation.UpdateAsync(entity, transaction, Operations.Update, ct);
        if (outcome == MutationResult.NoWrite) {
            // Nothing staged: the reservation releases and a retry re-runs deterministically.
            execution.MarkNotWritten();
            return new();
        }

        var detail = RequireDetail(_mapper.Map<TEntity, TDetail>(entity));
        SyncStamp(entity, detail, transaction);

        return new() { Detail = detail };
    }

    private async Task<UpdateResultBase<TDetail>> CreateMissingAsync(
        AdviceContext                 ctx,
        string                        name,
        TRequest                      request,
        ClaimsPrincipal?              principal,
        ResourceDispatchExecution?    execution,
        CancellationToken             ct
    ) {
        var container = new ResourceRequestContainer<TEntity>();
        ResourceIdentifiers.Apply(container, name);
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        if (descriptor.HasParent && descriptor.ParseCanonicalName(name) is { } addressed) {
            var routeValues = new Dictionary<string, object?>(addressed.ParentValues.Count);
            foreach (var parent in addressed.ParentValues) routeValues[parent.Key] = parent.Value;
            descriptor.SetParentFromRouteValues(request, routeValues);
        }

        // The create stages answer with a create-shaped handled result; the update operation
        // projects the produced detail onto its own response envelope.
        var requestOutput = new HandledResponse<CreateResultBase<TDetail>>();
        var requestResult = await HandledResponseRunner.RunAsync(
            requestOutput,
            () => Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest, TDetail>>()
                         .RunAsync(ctx, request, container, principal, requestOutput, ct), CollectionNotFound);
        if (requestResult is not null) {
            return new UpdateResultBase<TDetail> { Detail = requestResult.Detail };
        }

        var entity = _mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            throw new ValidationException([new() {
                Field       = nameof(request),
                Description = SchemataResources.GetResourceString(SchemataResources.INVALID_PAYLOAD),
                Reason      = SchemataResources.INVALID_PAYLOAD,
            }]);
        }
        AdviceApplyChildParent<TEntity, TRequest, TDetail>.Apply(request, entity);

        var entityOutput = new HandledResponse<CreateResultBase<TDetail>>();
        var entityResult = await HandledResponseRunner.RunAsync(
            entityOutput,
            () => Advisor.For<IResourceCreateAdvisor<TEntity, TRequest, TDetail>>()
                         .RunAsync(ctx, request, entity, principal, entityOutput, ct), CollectionNotFound);
        if (entityResult is not null) {
            return new UpdateResultBase<TDetail> { Detail = entityResult.Detail };
        }

        var mutation = _sp.GetRequiredService<IResourceMutation<TEntity>>();
        if (execution is null) {
            var outcome = await mutation.CreateAsync(entity, null, ct);

            // NoWrite covers a repository advisor blocking the add and a consumer advisor handling
            // the create as a no-op; either way no row exists, so the response carries no detail.
            if (outcome == MutationResult.NoWrite) {
                return new();
            }

            return new() { Detail = RequireDetail(_mapper.Map<TEntity, TDetail>(entity)) };
        }

        return await ExecuteOwnedAsync(
            execution,
            transaction => MutateMissingAsync(mutation, entity, transaction, execution, ct),
            ct);
    }

    private async Task<UpdateResultBase<TDetail>> MutateMissingAsync(
        IResourceMutation<TEntity> mutation,
        TEntity                    entity,
        IUnitOfWork                transaction,
        ResourceDispatchExecution  execution,
        CancellationToken          ct
    ) {
        var outcome = await mutation.CreateAsync(entity, transaction, ct);
        if (outcome == MutationResult.NoWrite) {
            // Nothing staged: the reservation releases and a retry re-runs deterministically.
            execution.MarkNotWritten();
            return new();
        }

        var detail = RequireDetail(_mapper.Map<TEntity, TDetail>(entity));
        SyncStamp(entity, detail, transaction);

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
