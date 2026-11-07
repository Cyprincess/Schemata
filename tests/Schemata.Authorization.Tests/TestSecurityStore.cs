using System;
using Moq;
using Schemata.Entity.Repository;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Tests;

/// <summary>
///     In-memory <see cref="ISecurityStore{TSecurity}" /> standing in for the host store in
///     unit tests. Rows order by create time descending then key ascending, mirroring the
///     store contract the token pipeline relies on for primary-key selection.
/// </summary>
public class TestSecurityStore : ISecurityStore<SchemataSecurity>, IApplicationSecurityMetadataStore
{
    private readonly List<SchemataSecurity> _rows;

    public TestSecurityStore(List<SchemataSecurity>? rows = null) {
        _rows = rows ?? [];
    }

    /// <summary>Number of <see cref="ListByParentAsync" /> invocations observed; proves an
    /// operation resolves its signing selection once instead of re-querying per token.</summary>
    public int ListByParentCalls { get; private set; }

    /// <summary>Number of rows currently held.</summary>
    public int Count => _rows.Count;

    /// <inheritdoc />
    public Task<SchemataSecurity?> FindByCanonicalNameAsync(string? canonicalName, CancellationToken ct = default) {
        return Task.FromResult(_rows.FirstOrDefault(row => row.CanonicalName == canonicalName));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SchemataSecurity> ListByParentAsync(
        string?           parent,
        string?           kind   = null,
        string?           usage  = null,
        string?           status = null,
        CancellationToken ct     = default
    ) {
        ListByParentCalls++;
        return Enumerate(_rows
            .Where(row => row.Parent == parent
                       && (kind is null || row.Kind == kind)
                       && (usage is null || row.Usage == usage)
                       && (status is null || row.Status == status))
            .OrderByDescending(row => row.CreateTime)
            .ThenBy(row => row.Key));
    }

    /// <inheritdoc />
    public async Task<SchemataSecurity?> CreateAsync(SchemataSecurity? security, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        if (security is null) {
            return null;
        }

        if (beforeCommit is not null) await beforeCommit(new Mock<IUnitOfWork>().Object, ct);
        _rows.Add(security);
        return security;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(SchemataSecurity? security, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        if (beforeCommit is not null) await beforeCommit(new Mock<IUnitOfWork>().Object, ct);
    }

    /// <inheritdoc />
    public Task DeleteAsync(SchemataSecurity? security, CancellationToken ct = default) {
        if (security is not null) {
            _rows.Remove(security);
        }

        return Task.CompletedTask;
    }

    public async Task ProjectAsync(SchemataApplication application, RegistrationResponse response, CancellationToken ct) {
        await foreach (var row in ListByParentAsync(application.CanonicalName, SecurityConstants.Kinds.Jwks,
                           null, SecurityConstants.Statuses.Valid, ct)) {
            using var document = JsonDocument.Parse(row.Value!);
            RegistrationMetadataMapper.ValidatePublicJwks(document.RootElement);
            response.Jwks = document.RootElement.Clone();
            break;
        }
        await foreach (var row in ListByParentAsync(application.CanonicalName, SecurityConstants.Kinds.JwksUri,
                           null, SecurityConstants.Statuses.Valid, ct)) {
            response.JwksUri = row.Value;
            break;
        }
    }

    public Task ReplaceAsync(
        SchemataApplication application,
        RegisterRequest request,
        IUnitOfWork transaction,
        CancellationToken ct
    ) {
        var stale = _rows.Where(row => row.Parent == application.CanonicalName
                                    && row.Kind is SecurityConstants.Kinds.Jwks or SecurityConstants.Kinds.JwksUri).ToList();
        foreach (var row in stale) {
            row.Status = SecurityConstants.Statuses.Revoked;
        }
        if (request.Jwks is { ValueKind: JsonValueKind.Object } jwks) {
            _rows.Add(new() {
                Parent = application.CanonicalName,
                Key = application.ClientId,
                Kind = SecurityConstants.Kinds.Jwks,
                Usage = SecurityConstants.Usages.Authentication,
                Value = jwks.GetRawText(),
                Status = SecurityConstants.Statuses.Valid,
            });
        }
        if (!string.IsNullOrWhiteSpace(request.JwksUri)) {
            _rows.Add(new() {
                Parent = application.CanonicalName,
                Key = application.ClientId,
                Kind = SecurityConstants.Kinds.JwksUri,
                Usage = SecurityConstants.Usages.Authentication,
                Value = request.JwksUri,
                Status = SecurityConstants.Statuses.Valid,
            });
        }
        return Task.CompletedTask;
    }

    private async IAsyncEnumerable<SchemataSecurity> Enumerate(
        IEnumerable<SchemataSecurity> rows,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        foreach (var row in rows) {
            await Task.Yield();
            yield return row;
        }
    }
}
