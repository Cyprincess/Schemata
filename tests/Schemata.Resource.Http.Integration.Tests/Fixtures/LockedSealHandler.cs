using System.Linq;
using Schemata.Messaging.Skeleton;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

public sealed class LockedSealHandler(Entity.Repository.IRepository<LockedStudent> repository)
    : IRequestHandler<LockedSealRequest, LockedStudent>
{
    public async System.Threading.Tasks.Task<LockedStudent> HandleAsync(
        LockedSealRequest                  request,
        System.Threading.CancellationToken ct = default) {
        var entity = await repository.SingleOrDefaultAsync(
            query => query.Where(student => student.CanonicalName == request.CanonicalName), ct);
        return entity ?? throw Common.Errors.SchemataResourceErrors.NotFound<LockedStudent>(request.CanonicalName);
    }
}