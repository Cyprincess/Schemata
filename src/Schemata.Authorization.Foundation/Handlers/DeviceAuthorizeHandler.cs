using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

public sealed class DeviceAuthorizeHandler<TApp, TToken>(
    IClientAuthenticationService<TApp>     client,
    ITokenManager<TToken>                  tokens,
    IOptions<SchemataAuthorizationOptions> options,
    IServiceProvider                       sp,
    IOptions<JsonSerializerOptions>        json
) : DeviceAuthorizeEndpoint
    where TApp : SchemataApplication
    where TToken : SchemataToken, new()
{
    private const string UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public override async Task<AuthorizationResult> DeviceAuthorizeAsync(
        DeviceAuthorizeRequest             request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId]     = [request.ClientId],
            [Parameters.ClientSecret] = [request.ClientSecret],
        }, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.GetResourceString(SchemataResources.ST4001));
        }

        var ctx = new AdviceContext(sp);

        switch (await Advisor.For<IDeviceAuthorizeAdvisor<TApp>>()
                             .RunAsync(ctx, application, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.GetResourceString(SchemataResources.ST4001));
        }

        var now    = DateTime.UtcNow;
        var expiry = now + options.Value.DeviceCodeLifetime;

        var dc = new TToken {
            ApplicationName = application.Name,
            Type            = TokenTypes.DeviceCode,
            Status          = TokenStatuses.Valid,
            ReferenceId     = GenerateDeviceCode(),
            Payload = JsonSerializer.Serialize(new DeviceCodePayload {
                Scope = request.Scope,
                ClientId = application.ClientId,
            }, json.Value),
            ExpireTime = expiry,
        };

        await tokens.CreateAsync(dc, ct);

        var uc = new TToken {
            ApplicationName = application.Name,
            Type            = TokenTypes.UserCode,
            Status          = TokenStatuses.Valid,
            ReferenceId     = GenerateUserCode(),
            Payload = JsonSerializer.Serialize(new UserCodePayload {
                DeviceCodeName = dc.Name,
                Scope = request.Scope,
                ClientId = application.ClientId,
            }, json.Value),
            ExpireTime = expiry,
        };

        await tokens.CreateAsync(uc, ct);

        var query = QueryString.Create(new Dictionary<string, string?> {
            { Parameters.UserCode, uc.ReferenceId },
            { Parameters.CodeType, TokenTypeUris.UserCode },
        });

        return AuthorizationResult.Content(new DeviceAuthorizationResponse {
            DeviceCode      = dc.ReferenceId,
            UserCode        = uc.ReferenceId,
            VerificationUri = options.Value.DeviceVerificationUri,
            VerificationUriComplete = !string.IsNullOrWhiteSpace(options.Value.DeviceVerificationUri)
                ? $"{options.Value.DeviceVerificationUri}{query.ToUriComponent()}"
                : null,
            ExpiresIn = (int)options.Value.DeviceCodeLifetime.TotalSeconds,
            Interval  = options.Value.DeviceCodeInterval,
        });
    }

    private static string GenerateDeviceCode() { return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)); }

    private static string GenerateUserCode() {
        Span<char> code = stackalloc char[9];

        for (var i = 0; i < 8; i++) {
            var offset = i >= 4 ? i + 1 : i;
            code[offset] = UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        }

        code[4] = '-';
        return new(code);
    }
}
