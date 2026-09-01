using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Integration.Tests.Resource.Fixtures;

public sealed class PreviewHandler(IRepository<Student> repository)
    : IRequestHandler<PreviewResourceRequest, Student>
{
    public async Task<Student> HandleAsync(
        PreviewResourceRequest request,
        CancellationToken      ct = default
    ) {
        var entity = await repository.SingleOrDefaultAsync(
            query => query.Where(student => student.CanonicalName == request.CanonicalName),
            ct);
        return entity ?? throw SchemataResourceErrors.NotFound<Student>(request.CanonicalName);
    }
}
