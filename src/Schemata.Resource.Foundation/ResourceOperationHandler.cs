using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Internal;
using Schemata.Resource.Foundation.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Orchestrates standard CRUD operations including
///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>,
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>,
///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>,
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>, and
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso> by running an advisor
///     pipeline around each
///     step: general request check -> operation-specific request advisor -> entity advisor -> persistence -> response
///     advisor.
/// </summary>
/// <typeparam name="TEntity">
///     The persistent entity type implementing <see cref="ICanonicalName" />.
/// </typeparam>
/// <typeparam name="TRequest">The request DTO for create/update operations.</typeparam>
/// <typeparam name="TDetail">The detail DTO returned from get, create, and update.</typeparam>
/// <typeparam name="TSummary">The summary DTO returned from list operations.</typeparam>
public sealed partial class ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    private readonly ISimpleMapper        _mapper;
    private readonly IRepository<TEntity> _repository;
    private readonly IServiceProvider     _sp;

    /// <summary>
    ///     Initializes a new instance with its required dependencies.
    /// </summary>
    /// <param name="sp">The <see cref="IServiceProvider" /> for resolving advisors and options.</param>
    /// <param name="repository">The entity repository.</param>
    /// <param name="mapper">The mapper for entity-DTO conversion.</param>
    public ResourceOperationHandler(IServiceProvider sp, IRepository<TEntity> repository, ISimpleMapper mapper) {
        _sp         = sp;
        _repository = repository;
        _mapper     = mapper;
    }

    private IDataProtector Protector => field ??= _sp
                                                 .GetRequiredService<IDataProtectionProvider>()
                                                 .CreateProtector(PageToken.ProtectionPurpose);

    /// <summary>
    ///     Creates the standard missing-resource exception for the target entity type.
    /// </summary>
    /// <param name="name">The requested resource name.</param>
    /// <returns>An exception carrying missing-resource error details.</returns>
    internal static NotFoundException ResourceNotFound(string? name) {
        return SchemataResourceErrors.NotFound<TEntity>(name);
    }

    private static NotFoundException CollectionNotFound() {
        return ResourceNotFound(ResourceNameDescriptor.ForType<TEntity>().Collection);
    }

    private static void StashReadMask(AdviceContext ctx, string? mask) {
        if (string.IsNullOrWhiteSpace(mask) || mask.Trim() == Wildcards.Any) {
            return;
        }

        ctx.Set(new ReadMaskRequested(mask));
    }

    private TotalSizeMode ResolveTotalSizeMode() {
        var options = _sp.GetService<IOptions<SchemataResourceOptions>>()?.Value;
        if (options is null) {
            return TotalSizeMode.Exact;
        }

        if (options.Resources.TryGetValue(typeof(TEntity).TypeHandle, out var resource)
         && resource.TotalSize is not TotalSizeMode.Default) {
            return resource.TotalSize;
        }

        return options.TotalSize is TotalSizeMode.Default ? TotalSizeMode.Exact : options.TotalSize;
    }

    private AdviceContext CreateAdviceContext() { return ResourceAdviceContext.Create(_sp); }

    private static Task<TResult?> RunPipelineAsync<TResult>(
        AdviceContext            ctx,
        Func<Task<AdviseResult>> advise,
        Func<Exception>          blocked,
        Func<TResult>?           handled = null
    )
        where TResult : class {
        return ResourcePipelineRunner<Operations>.RunAsync(ctx, advise, blocked, handled);
    }
}
