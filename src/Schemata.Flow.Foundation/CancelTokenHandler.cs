using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CancelProcessTokenRequest = Schemata.Flow.Foundation.Commands.CancelTokenRequest;
using Schemata.Flow.Foundation.Commands;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Handles token-cancellation requests dispatched through the resource-method pipeline.
/// </summary>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
public sealed class CancelTokenHandler<TToken>(
    IRequestDispatcher dispatcher,
    IRepository<TToken> tokens)
    : IRequestHandler<CancelTokenResourceRequest, ProcessSnapshot>
    where TToken : SchemataProcessToken
{
    public async Task<ProcessSnapshot> HandleAsync(
        CancelTokenResourceRequest request,
        AdviceContext context,
        CancellationToken ct = default)
    {
        var name = request.CanonicalName
            ?? throw new InvalidOperationException("Instance method requires a target canonical name.");

        var token = await tokens.SingleOrDefaultAsync(
            q => q.Where(t => t.CanonicalName == name), ct, new QueryOptions(IgnoreSoftDelete: true));

        if (token is null) {
            throw SchemataResourceErrors.NotFound<TToken>(name);
        }

        return await dispatcher.SendAsync<CancelProcessTokenRequest, ProcessSnapshot>(
            new($"processes/{token.Process}", name, request.Principal), ct);
    }
}
