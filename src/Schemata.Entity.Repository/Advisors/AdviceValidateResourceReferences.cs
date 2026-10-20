using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Common.Errors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceValidateResourceReferences{TEntity}" />.</summary>
public static class AdviceValidateResourceReferences
{
    /// <summary>
    ///     Default execution order: runs after the validation advisor so structural
    ///     entity validation has cleared, but before uniqueness so a typed reference
    ///     mismatch surfaces as <c>NOT_FOUND</c> rather than a duplicate lookup.
    /// </summary>
    public const int DefaultOrder = AdviceAddValidation.DefaultOrder + 10_000_000;

    internal static IResourceTypeResolver RequireResolver(AdviceContext ctx, Type entity, string? detail = null) {
        return ctx.ServiceProvider.GetService<IResourceTypeResolver>()
            ?? throw new InvalidOperationException(
                $"{entity.Name}{detail} requires {nameof(IResourceTypeResolver)} for {nameof(ResourceReferenceAttribute)}.");
    }

    /// <summary>
    ///     Validates a typed reference without a resolver: the value must match the declared
    ///     target's own <see cref="CanonicalNameAttribute" /> pattern. Host-defined subtypes that
    ///     keep the built-in pattern satisfy the reference; a subtype with a different pattern
    ///     needs a resolver to be recognized as assignable.
    /// </summary>
    internal static bool MatchesDeclaredTarget(Type target, string value) {
        if (!typeof(ICanonicalName).IsAssignableFrom(target)) {
            return false;
        }

        return ResourceNameDescriptor.ForType(target).ParseCanonicalName(value) is not null;
    }
}

/// <summary>
///     Validates every <see cref="ResourceReferenceAttribute" /> property on the entity
///     against the registered <see cref="IResourceTypeResolver" />.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             Typed references (<see cref="ResourceReferenceAttribute.Target" /> non-<see langword="null" />)
///             are validated against the declared target: when an <see cref="IResourceTypeResolver" />
///             is registered the resolved type must be assignable to the target, so a host-defined
///             subtype satisfies a reference declared against its built-in base; without a resolver
///             the value must match the target's own <see cref="CanonicalNameAttribute" /> pattern,
///             so repository-only hosts still validate the framework's typed references. An invalid
///             value throws
///             <see cref="SchemataResourceErrors.NotFound(System.Type, string?, string?, string)" /> for
///             the target type so callers see <c>NOT_FOUND</c> with a populated
///             <see cref="Schemata.Abstractions.Errors.ResourceInfoDetail" />.
///         </item>
///         <item>
///             Polymorphic references (<see cref="ResourceReferenceAttribute.Target" /> <see langword="null" />)
///             require an <see cref="IResourceTypeResolver" />: absolute URI identities pass
///             through, an unresolvable value is collected into a
///             <see cref="ValidationException" /> with
///             <see cref="ErrorFieldViolation.Reason" /> <c>INVALID_REFERENCE</c>, and a missing
///             resolver throws a configuration <see cref="InvalidOperationException" />.
///         </item>
///     </list>
///     Null or empty values pass through; nullability of the reference field is governed
///     by the FluentValidation pipeline, not this advisor.
/// </remarks>
/// <typeparam name="TEntity">The entity type being added or updated.</typeparam>
public sealed class AdviceValidateResourceReferences<TEntity> :
    IRepositoryAddAdvisor<TEntity>,
    IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    private static readonly Lazy<IReadOnlyList<Reference>> References = new(BuildReferences);

    /// <inheritdoc cref="IRepositoryAddAdvisor{TEntity}" />
    public int Order => AdviceValidateResourceReferences.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        MutationOptions      options,
        CancellationToken    ct = default) {
        return Task.FromResult(Validate(ctx, entity));
    }

    private static AdviseResult Validate(AdviceContext ctx, TEntity entity) {
        var references = References.Value;
        if (references.Count == 0) {
            return AdviseResult.Continue;
        }

        var resolver = ctx.ServiceProvider.GetService<IResourceTypeResolver>();

        List<ErrorFieldViolation>? violations = null;

        foreach (var reference in references) {
            if (reference.Property.GetValue(entity) is not string value || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (reference.Target is not null) {
                if (IsTypedReferenceValid(reference.Target, value, resolver)) {
                    continue;
                }

                throw SchemataResourceErrors.NotFound(reference.Target, value);
            }

            // Canonical references are {collection}/{id}-shaped and never carry a scheme;
            // an absolute URI is an identity (e.g. an issuer), not a resource reference.
            // Untyped references only: typed references keep resolving and validating.
            if (Uri.TryCreate(value, UriKind.Absolute, out _)) {
                continue;
            }

            resolver ??= AdviceValidateResourceReferences.RequireResolver(ctx, typeof(TEntity), $".{reference.Property.Name}");

            if (resolver.Resolve(value) is not null) {
                continue;
            }

            violations ??= [];
            violations.Add(new() {
                Field       = reference.Property.Name,
                Reason      = SchemataResources.INVALID_REFERENCE,
                Description = LocalizedMessageFormatter.FormatInvariant(
                    SchemataResources.INVALID_REFERENCE,
                    new Dictionary<string, string?> { ["value"] = value }),
            });
        }

        if (violations is { Count: > 0 }) {
            throw new ValidationException(violations);
        }

        return AdviseResult.Continue;
    }

    private static bool IsTypedReferenceValid(Type target, string value, IResourceTypeResolver? resolver) {
        if (resolver is null) {
            return AdviceValidateResourceReferences.MatchesDeclaredTarget(target, value);
        }

        var resolved = resolver.Resolve(value);
        return resolved is not null && target.IsAssignableFrom(resolved);
    }

    private static IReadOnlyList<Reference> BuildReferences() {
        var list = new List<Reference>();
        foreach (var property in typeof(TEntity).GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            if (property.GetIndexParameters().Length > 0) {
                continue;
            }

            var attribute = property.GetCustomAttribute<ResourceReferenceAttribute>(true);
            if (attribute is null) {
                continue;
            }

            if (property.PropertyType != typeof(string)) {
                continue;
            }

            list.Add(new(property, attribute.Target));
        }

        return list;
    }

    #region Nested type: Reference

    private sealed record Reference(PropertyInfo Property, Type? Target);

    #endregion
}
