using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Owns the security-row representation of registration <c>jwks</c> and <c>jwks_uri</c>.</summary>
public sealed class ApplicationSecurityMetadataStore<TSecurity>(
    ISecurityStore<TSecurity> securities,
    IResourceMutation<TSecurity> mutation
) : IApplicationSecurityMetadataStore
    where TSecurity : SchemataSecurity, new()
{
    public async Task ProjectAsync(SchemataApplication application, RegistrationResponse response, CancellationToken ct) {
        var parent = SecurityParents.Application(application);
        await foreach (var row in securities.ListByParentAsync(
                           parent, SecurityConstants.Kinds.Jwks, null, SecurityConstants.Statuses.Valid, ct)) {
            if (string.IsNullOrWhiteSpace(row.Value)) {
                throw new InvalidOperationException("The active registration JWKS row has no key document.");
            }

            using var document = JsonDocument.Parse(row.Value);
            RegistrationMetadataMapper.ValidatePublicJwks(document.RootElement);
            response.Jwks = document.RootElement.Clone();
            break;
        }

        await foreach (var row in securities.ListByParentAsync(
                           parent, SecurityConstants.Kinds.JwksUri, null, SecurityConstants.Statuses.Valid, ct)) {
            response.JwksUri = row.Value;
            break;
        }
    }

    public async Task ReplaceAsync(
        SchemataApplication application,
        RegisterRequest request,
        IUnitOfWork transaction,
        CancellationToken ct
    ) {
        var parent = SecurityParents.Application(application);
        var stale = new List<TSecurity>();
        await foreach (var row in securities.ListByParentAsync(parent, SecurityConstants.Kinds.Jwks, null, null, ct)) {
            stale.Add(row);
        }
        await foreach (var row in securities.ListByParentAsync(parent, SecurityConstants.Kinds.JwksUri, null, null, ct)) {
            stale.Add(row);
        }

        foreach (var row in stale) {
            row.Status = SecurityConstants.Statuses.Revoked;
            if (await mutation.UpdateAsync(row, transaction, Operations.Update, ct) != MutationResult.Applied) {
                throw new InvalidOperationException("The superseded client key metadata was not staged.");
            }
        }

        if (request.Jwks is { ValueKind: JsonValueKind.Object } jwks) {
            await StageAsync(new() {
                Parent = parent,
                Key = application.ClientId,
                Kind = SecurityConstants.Kinds.Jwks,
                Usage = SecurityConstants.Usages.Authentication,
                Value = jwks.GetRawText(),
                Status = SecurityConstants.Statuses.Valid,
            }, transaction, ct);
        }

        if (!string.IsNullOrWhiteSpace(request.JwksUri)) {
            await StageAsync(new() {
                Parent = parent,
                Key = application.ClientId,
                Kind = SecurityConstants.Kinds.JwksUri,
                Usage = SecurityConstants.Usages.Authentication,
                Value = request.JwksUri,
                Status = SecurityConstants.Statuses.Valid,
            }, transaction, ct);
        }
    }

    private async Task StageAsync(TSecurity row, IUnitOfWork transaction, CancellationToken ct) {
        if (await mutation.CreateAsync(row, transaction, ct) != MutationResult.Applied) {
            throw new InvalidOperationException("The client key metadata was not staged.");
        }
    }
}
