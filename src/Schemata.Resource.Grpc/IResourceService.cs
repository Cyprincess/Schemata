using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Grpc;

/// <summary>
///     gRPC service contract for CRUD operations on a resource, used by protobuf-net code-first gRPC.
/// </summary>
/// <typeparam name="TEntity">The persistent entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type for create and update operations.</typeparam>
/// <typeparam name="TDetail">The detail DTO type returned from get, create, and update operations.</typeparam>
/// <typeparam name="TSummary">The summary DTO type returned from list operations.</typeparam>
[Service]
public interface IResourceService<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     Lists resources with filtering, ordering, and pagination.
    /// </summary>
    /// <param name="request">The list request parameters.</param>
    /// <param name="context">The gRPC call context.</param>
    /// <returns>A paginated list result with summaries.</returns>
    [Operation]
    ValueTask<ListResult<TSummary>> ListAsync(ListRequest request, CallContext context = default);

    /// <summary>
    ///     Gets a single resource by canonical name.
    /// </summary>
    /// <param name="request">The get request containing the canonical name.</param>
    /// <param name="context">The gRPC call context.</param>
    /// <returns>The resource detail DTO.</returns>
    [Operation]
    ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default);

    /// <summary>
    ///     Creates a new resource.
    /// </summary>
    /// <param name="request">The creation request DTO.</param>
    /// <param name="context">The gRPC call context.</param>
    /// <returns>The created resource detail DTO.</returns>
    [Operation]
    ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default);

    /// <summary>
    ///     Updates an existing resource by canonical name.
    /// </summary>
    /// <param name="request">The update request DTO.</param>
    /// <param name="context">The gRPC call context.</param>
    /// <returns>The updated resource detail DTO.</returns>
    [Operation]
    ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default);

    /// <summary>
    ///     Deletes a resource by canonical name.
    /// </summary>
    /// <param name="request">The delete request containing the canonical name, ETag, and force flag.</param>
    /// <param name="context">The gRPC call context.</param>
    [Operation]
    ValueTask DeleteAsync(DeleteRequest request, CallContext context = default);
}
