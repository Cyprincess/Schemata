using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CancelProcessTokenRequest = Schemata.Flow.Foundation.Commands.CancelTokenRequest;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Handles token-cancellation requests dispatched through the resource-method pipeline.
/// </summary>
public sealed class CancelTokenHandler(
    IRequestDispatcher                    dispatcher,
    IRepository<SchemataProcessToken> tokens)
    : IRequestHandler<CancelTokenResourceRequest, ProcessSnapshot>
{
    /// <inheritdoc />
    public async Task<ProcessSnapshot> HandleAsync(
        CancelTokenResourceRequest request,
        CancellationToken ct = default)
    {
        var name = request.CanonicalName
            ?? throw new InvalidOperationException("Instance method requires a target canonical name.");

        SchemataProcessToken? token;
        using (tokens.SuppressQuerySoftDelete()) {
            token = await tokens.SingleOrDefaultAsync(
                q => q.Where(t => t.CanonicalName == name), ct);
        }

        if (token is null) {
            throw SchemataResourceErrors.NotFound<SchemataProcessToken>(name);
        }

        return await dispatcher.SendAsync<CancelProcessTokenRequest, ProcessSnapshot>(
            new($"processes/{token.Process}", name, request.Principal), ct);
    }
}
