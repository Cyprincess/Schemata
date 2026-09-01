using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Runtime;

/// <summary>
///     Dispatches commands and queries inside the current process: establishes the ambient
///     <see cref="AdviceContext" /> for the call, composes the registered
///     <see cref="IRequestPipelineAdvisor{TRequest,TResponse}" /> chain around the request's single
///     handler for a <see cref="ICommand" />, <see cref="ICommand{TResult}" /> or
///     <see cref="IQuery{TResult}" />, and invokes it. A plain <see cref="IRequest{TResponse}" />
///     that is neither a command nor a query runs no chain and falls straight through to the handler.
/// </summary>
/// <remarks>
///     Public and unifying: a single implementation answers <see cref="ICommandDispatcher" />,
///     <see cref="IQueryDispatcher" /> and the plain <see cref="IRequestDispatcher" />. The handler
///     is resolved lazily at the tail of the chain, so an advisor that short-circuits without calling
///     its continuation never triggers the missing-handler guard.
/// </remarks>
/// <param name="services">
///     The root <see cref="IServiceProvider" /> used both to resolve the request's handler and to
///     seed each dispatch's <see cref="AdviceContext" />.
/// </param>
public sealed class InProcessRequestDispatcher(IServiceProvider services) : ICommandDispatcher, IQueryDispatcher
{
    /// <inheritdoc cref="IRequestDispatcher.SendAsync{TRequest,TResponse}" />
    public async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse> {
        var ctx = new AdviceContext(services);
        using var _ = AdviceContext.Establish(ctx);

        Task<TResponse> Handle(CancellationToken token) {
            var handlers = services.GetServices<IRequestHandler<TRequest, TResponse>>().ToList();

            return handlers.Count switch {
                1 => handlers[0].HandleAsync(request, token),
                0 => throw new InvalidOperationException(
                    $"No request handler registered for request type '{typeof(TRequest).FullName}'."),
                _ => throw new InvalidOperationException(
                    $"Multiple request handlers registered for request type '{typeof(TRequest).FullName}'. Expected exactly one."),
            };
        }

        if (request is not (ICommand or ICommand<TResponse> or IQuery<TResponse>)) {
            return await Handle(ct);
        }

        var advisors = services.GetServices<IRequestPipelineAdvisor<TRequest, TResponse>>()
                               .OrderBy(advisor => advisor.Order)
                               .ToList();

        RequestHandlerContinuation<TResponse> next = Handle;
        for (var i = advisors.Count - 1; i >= 0; i--) {
            var advisor    = advisors[i];
            var downstream = next;
            next = token => advisor.AdviseAsync(ctx, request, downstream, token);
        }

        return await next(ct);
    }

    /// <inheritdoc cref="ICommandDispatcher.SendAsync{TCommand}" />
    public Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand
        => SendAsync<TCommand, Unit>(command, ct);
}
