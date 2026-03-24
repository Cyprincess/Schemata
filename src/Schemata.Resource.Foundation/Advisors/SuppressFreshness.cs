namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
/// Marker type that, when set in the advice context, suppresses all freshness (ETag) checks and generation.
/// </summary>
/// <remarks>
/// When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext"/>,
/// <see cref="AdviceUpdateFreshness{TEntity, TRequest}"/>, <see cref="AdviceDeleteFreshness{TEntity}"/>,
/// and <see cref="AdviceResponseFreshness{TEntity, TDetail}"/> all skip their logic.
/// Automatically set when <see cref="SchemataResourceOptions.SuppressFreshness"/> is <see langword="true"/>.
/// </remarks>
internal sealed class SuppressFreshness;
