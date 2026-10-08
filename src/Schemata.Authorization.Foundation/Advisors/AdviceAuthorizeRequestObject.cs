using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Verifies and folds in a JAR <c>request</c> JWT, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9101.html#section-4.1">
///         RFC 9101: JWT-Secured Authorization Request (JAR) §4.1: Request Object Construction
///     </seealso>
///     . Runs after the PAR <c>request_uri</c> advisor so the recovered query parameters
///     reach the JWT-bound client. A <c>dpop_jkt</c> recovered from the stored PAR payload is
///     the binding committed at the PAR endpoint: the request object may confirm the same
///     thumbprint, and is rejected when it carries a different one (RFC 9449 §10).
/// </summary>
public sealed class AdviceAuthorizeRequestObject<TApp>(
    IApplicationManager<TApp>              apps,
    RequestObjectReader<TApp>              reader,
    IOptions<JwtSecuredAuthorizationRequestsOptions> options
) : IAuthorizeRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    public int Order => AdviceAuthorizeRequestObject.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        var request = authz.Request;
        if (request is null) {
            return AdviseResult.Continue;
        }

        var application = authz.Application ?? await apps.FindByClientIdAsync(request.ClientId, ct);
        var requireSigned = options.Value.RequireForAllClients
                          || application?.RequireSignedRequestObject == true;


        if (string.IsNullOrWhiteSpace(request.Request)) {
            if (requireSigned) {
                throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.Request });
            }

            return AdviseResult.Continue;
        }

        if (application is null) {
            throw new OAuthException(OAuthErrors.InvalidRequestObject, SchemataResources.INVALID_REQUEST_OBJECT);
        }

        authz.Application = application;

        // The reader overwrites target parameters with the object's claims, dropping the
        // PAR-stored dpop_jkt when the object omits it; capture the trusted binding first.
        var bound = authz.ResolvedRequestUri is not null ? request.DpopJkt : null;

        await reader.ReadAsync(request.Request, application, request, authz.Stage != AuthorizationRequestStage.Pushed, ct);

        if (!string.IsNullOrWhiteSpace(bound)) {
            if (string.IsNullOrWhiteSpace(request.DpopJkt)) {
                request.DpopJkt = bound;
            } else if (!string.Equals(request.DpopJkt, bound, StringComparison.Ordinal)) {
                throw new OAuthException(OAuthErrors.InvalidDpopProof, SchemataResources.DPOP_JKT_MISMATCH);
            }
        }

        authz.ResolvedRequestObject = request.Request;
        authz.ResponseMode = ResponseModeService.ResolveMode(request.ResponseMode, request.ResponseType);

        return AdviseResult.Continue;
    }
}

public static class AdviceAuthorizeRequestObject
{
    public const int DefaultOrder = AdviceAuthorizeClientAndRedirect.DefaultOrder - 1_000;
}