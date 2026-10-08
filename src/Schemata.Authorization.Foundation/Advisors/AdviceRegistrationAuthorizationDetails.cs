using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

internal sealed class AdviceRegistrationAuthorizationDetails<TApp>(IEnumerable<IAuthorizationDetailTypeDescriptor> descriptors)
    : IRegistrationRequestAdvisor<TApp>, IRegistrationResponseAdvisor<TApp> where TApp : SchemataApplication
{
    public int Order => SchemataConstants.Orders.Extension;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, RegisterRequest request, TApp application, CancellationToken ct = default) {
        if (request.AuthorizationDetailsTypes is { Count: > 0 } types) {
            var supported = descriptors.Select(d => d.Type).ToHashSet(StringComparer.Ordinal);
            if (types.Any(type => !supported.Contains(type))) {
                throw new OAuthException(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
            }
        }
        application.AuthorizationDetailsTypes = request.AuthorizationDetailsTypes;
        return Task.FromResult(AdviseResult.Continue);
    }

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, TApp application, RegistrationResponse response, CancellationToken ct = default) {
        response.AuthorizationDetailsTypes = application.AuthorizationDetailsTypes?.ToList();
        return Task.FromResult(AdviseResult.Continue);
    }
}
