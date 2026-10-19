using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Advisors;

internal sealed class AdviceRegistrationPushedAuthorizationRequests<TApp>
    : IRegistrationRequestAdvisor<TApp>, IRegistrationResponseAdvisor<TApp>
    where TApp : SchemataApplication
{
    public int Order => SchemataConstants.Orders.Extension;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        RegisterRequest   request,
        TApp              application,
        CancellationToken ct = default
    ) {
        application.RequirePushedAuthorizationRequests = request.RequirePushedAuthorizationRequests;
        return Task.FromResult(AdviseResult.Continue);
    }

    public Task<AdviseResult> AdviseAsync(
        AdviceContext         ctx,
        TApp                  application,
        RegistrationResponse response,
        CancellationToken     ct = default
    ) {
        response.RequirePushedAuthorizationRequests = application.RequirePushedAuthorizationRequests;
        return Task.FromResult(AdviseResult.Continue);
    }
}
