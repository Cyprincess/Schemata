using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Grpc;

[Service]
public interface IResourceService<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    [Operation]
    ValueTask<ListResult<TSummary>> ListAsync(ListRequest request, CallContext context = default);

    [Operation]
    ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default);

    [Operation]
    ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default);

    [Operation]
    ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default);

    [Operation]
    ValueTask DeleteAsync(DeleteRequest request, CallContext context = default);
}
