using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Internal;

/// <summary>
///     Dispatches commands and queries inside the current process: establishes the ambient
///     <see cref="AdviceContext" /> for the call, runs the matching advisor chain — the
///     <see cref="ICommandAdvisor{TCommand}" /> chain for a <see cref="ICommand" /> or
///     <see cref="ICommand{TResult}" />, the <see cref="IQueryAdvisor{TQuery}" /> chain for a
///     <see cref="IQuery{TResult}" /> — then invokes the request's single handler.
/// </summary>
/// <remarks>
///     Public and unifying: a single implementation answers <see cref="ICommandDispatcher" />,
///     <see cref="IQueryDispatcher" /> and the plain <see cref="IRequestDispatcher" />, so a caller
///     that only holds an <see cref="IRequest{TResponse}" /> that is neither a command nor a query
///     still dispatches through the same path, just without an advisor chain.
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

        if (request is ICommand or ICommand<TResponse>) {
            switch (await Advisor.For<ICommandAdvisor<TRequest>>().RunAsync(ctx, request, ct)) {
                case AdviseResult.Continue:
                    break;
                case AdviseResult.Handle when ctx.TryGet<TResponse>(out var handled) && handled is not null:
                    return handled;
                case AdviseResult.Handle:
                    throw new InvalidOperationException(
                        $"A command advisor for {typeof(TRequest)} returned Handle without setting a {typeof(TResponse)} result.");
                default:
                    throw new InvalidOperationException($"A command advisor blocked {typeof(TRequest)}.");
            }
        } else if (request is IQuery<TResponse>) {
            switch (await Advisor.For<IQueryAdvisor<TRequest>>().RunAsync(ctx, request, ct)) {
                case AdviseResult.Continue:
                    break;
                case AdviseResult.Handle when ctx.TryGet<TResponse>(out var handled) && handled is not null:
                    return handled;
                case AdviseResult.Handle:
                    throw new InvalidOperationException(
                        $"A query advisor for {typeof(TRequest)} returned Handle without setting a {typeof(TResponse)} result.");
                default:
                    throw new InvalidOperationException($"A query advisor blocked {typeof(TRequest)}.");
            }
        }

        var handlers = services.GetServices<IRequestHandler<TRequest, TResponse>>().ToList();

        return handlers.Count switch {
            1 => await handlers[0].HandleAsync(request, ct),
            0 => throw new InvalidOperationException(
                $"No request handler registered for request type '{typeof(TRequest).FullName}'."),
            _ => throw new InvalidOperationException(
                $"Multiple request handlers registered for request type '{typeof(TRequest).FullName}'. Expected exactly one."),
        };
    }

    /// <inheritdoc cref="ICommandDispatcher.SendAsync{TCommand}" />
    public Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand
        => SendAsync<TCommand, Unit>(command, ct);
}
