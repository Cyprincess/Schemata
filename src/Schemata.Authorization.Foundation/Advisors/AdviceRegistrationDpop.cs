using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Advisors;

internal sealed class AdviceRegistrationDpop<TApp> : IRegistrationRequestAdvisor<TApp>, IRegistrationResponseAdvisor<TApp>
    where TApp : SchemataApplication
{
    public int Order => SchemataConstants.Orders.Extension;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, RegisterRequest request, TApp application, CancellationToken ct = default) {
        application.DpopBoundAccessTokens = request.DpopBoundAccessTokens ?? false;
        return Task.FromResult(AdviseResult.Continue);
    }

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, TApp application, RegistrationResponse response, CancellationToken ct = default) {
        response.DpopBoundAccessTokens = application.DpopBoundAccessTokens;
        return Task.FromResult(AdviseResult.Continue);
    }
}
