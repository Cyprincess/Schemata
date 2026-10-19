using Schemata.Abstractions.Advisors;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises an identity request with the requested operation and caller. An advisor receives
///     the <see cref="IdentityOperationExecution{TResponse}" /> payload, writes a short-circuit
///     result to <see cref="IHandledResponse{TResponse}.Response" />, and returns
///     <see cref="Schemata.Abstractions.Advisors.AdviseResult.Handle" />.
/// </summary>
/// <typeparam name="TRequest">The request model type.</typeparam>
/// <typeparam name="TResponse">The operation's result payload type.</typeparam>
public interface IIdentityRequestAdvisor<in TRequest, TResponse>
    : IAdvisor<TRequest, IdentityOperationExecution<TResponse>>
    where TRequest : class
    where TResponse : class;
