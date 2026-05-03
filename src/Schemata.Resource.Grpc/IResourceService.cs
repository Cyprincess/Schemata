using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Code-first gRPC service contract for resource CRUD including
///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>,
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>,
///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>,
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>, and
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>; implemented by
///     <seealso cref="ResourceService{TEntity,TRequest,TDetail,TSummary}" />.
/// </summary>
[Service]
public interface IResourceService<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     Lists resources with filtering, ordering, and pagination
    ///     per <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>.
    /// </summary>
    /// <param name="request">The <see cref="ListRequest" /> with filter, page size, and page token.</param>
    /// <param name="context">The <see cref="CallContext" /> carrying gRPC metadata.</param>
    /// <returns>A <see cref="ListResult{TSummary}" /> with the matching resource summaries.</returns>
    [Operation]
    ValueTask<ListResult<TSummary>> ListAsync(ListRequest request, CallContext context = default);

    /// <summary>
    ///     Gets a single resource by canonical name
    ///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>.
    /// </summary>
    /// <param name="request">The <see cref="GetRequest" /> containing the canonical name.</param>
    /// <param name="context">The <see cref="CallContext" /> carrying gRPC metadata.</param>
    /// <returns>The resource detail DTO.</returns>
    [Operation]
    ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default);

    /// <summary>
    ///     Creates a new resource
    ///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>.
    /// </summary>
    /// <param name="request">The creation request DTO.</param>
    /// <param name="context">The <see cref="CallContext" /> carrying gRPC metadata.</param>
    /// <returns>The created resource detail DTO.</returns>
    [Operation]
    ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default);

    /// <summary>
    ///     Updates an existing resource by canonical name
    ///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>.
    /// </summary>
    /// <param name="request">The update request DTO.</param>
    /// <param name="context">The <see cref="CallContext" /> carrying gRPC metadata.</param>
    /// <returns>The updated resource detail DTO.</returns>
    [Operation]
    ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default);

    /// <summary>
    ///     Deletes a resource by canonical name
    ///     per <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>.
    /// </summary>
    /// <param name="request">
    ///     The <see cref="DeleteRequest" /> containing the canonical name, ETag for concurrency, and force flag.
    /// </param>
    /// <param name="context">The <see cref="CallContext" /> carrying gRPC metadata.</param>
    [Operation]
    ValueTask DeleteAsync(DeleteRequest request, CallContext context = default);
}
