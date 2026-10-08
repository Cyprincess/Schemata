using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

/// <summary>Evaluates whether a principal can perform an operation on an entity.</summary>
/// <typeparam name="T">Entity type being authorized.</typeparam>
/// <typeparam name="TRequest">Request payload type used by the operation.</typeparam>
public interface IAccessProvider<T, TRequest>
{
    /// <summary>
    ///     Evaluates access for the requested operation. <paramref name="entity" /> is the
    ///     loaded instance at <see cref="AccessStage.Instance" /> and <see langword="null" />
    ///     at <see cref="AccessStage.Target" /> and <see cref="AccessStage.Missing" />;
    ///     <paramref name="context" /> carries the stage, the requested target name, and the
    ///     applicable parent. A provider that cannot decide returns
    ///     <see cref="AccessDecision.Indeterminate" /> — callers fail closed on it.
    /// </summary>
    /// <param name="entity">Entity instance being authorized, when loaded.</param>
    /// <param name="context">Stage, operation, target, and request details.</param>
    /// <param name="principal">Principal requesting access.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<AccessDecision> HasAccessAsync(
        T?                      entity,
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    );
}
