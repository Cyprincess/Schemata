namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Carries the AIP-136 custom method verb through the
///     <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> so that
///     downstream method advisors (anonymous / authorize / idempotency /
///     freshness) can key their behavior on the specific verb being invoked.
///     Set by
///     <see cref="Schemata.Resource.Foundation.ResourceMethodOperationHandler{TEntity, TRequest, TResponse}" />
///     before the request gate runs.
/// </summary>
/// <param name="Verb">The verb in lowerCamelCase as declared by
///     <see cref="Schemata.Abstractions.Resource.ResourceMethodAttribute" />.</param>
public sealed record ResourceMethodVerb(string Verb);
