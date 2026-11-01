using System;
using System.Threading.Tasks;

namespace Schemata.Abstractions.Advisors;

/// <summary>
///     Executes one advisor stage and reads the handled response off its
///     <see cref="IHandledResponse{TResponse}" /> carrier.
/// </summary>
public static class HandledResponseRunner
{
    /// <summary>
    ///     Runs the stage and returns the handled response when the stage short-circuits.
    /// </summary>
    /// <remarks>
    ///     A stage that continues leaves no handled response: an advisor writes one only when it
    ///     returns <see cref="AdviseResult.Handle" />. A component that keeps one carrier across
    ///     several stages clears <see cref="IHandledResponse{TResponse}.Response" /> before the next
    ///     stage, so a response written by an advisor that then continued cannot stand in for a
    ///     later stage's result.
    /// </remarks>
    /// <typeparam name="TResponse">The response type carried by handled advisor results.</typeparam>
    /// <param name="output">The stage carrier whose handled response is returned.</param>
    /// <param name="advise">The advisor stage to execute.</param>
    /// <param name="blocked">Factory for the exception raised when the stage blocks or handles without a response.</param>
    /// <param name="fallback">Response used when the stage handles without writing one.</param>
    /// <returns>The handled response, or <see langword="null" /> when processing should continue.</returns>
    public static async Task<TResponse?> RunAsync<TResponse>(
        IHandledResponse<TResponse> output,
        Func<Task<AdviseResult>>    advise,
        Func<Exception>             blocked,
        Func<TResponse>?            fallback = null
    ) where TResponse : class {
        switch (await advise()) {
            case AdviseResult.Continue:
                return null;
            case AdviseResult.Handle:
                if (output.Response is { } response) {
                    return response;
                }

                return fallback is null ? throw blocked() : fallback();
            case AdviseResult.Block:
            default:
                throw blocked();
        }
    }
}
