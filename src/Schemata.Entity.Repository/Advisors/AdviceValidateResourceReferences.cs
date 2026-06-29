using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceValidateResourceReferences{TEntity}" />.</summary>
public static class AdviceValidateResourceReferences
{
    /// <summary>
    ///     Default execution order: runs after the validation advisor so structural
    ///     entity validation has cleared, but before uniqueness so a typed reference
    ///     mismatch surfaces as <c>NOT_FOUND</c> rather than a duplicate lookup.
    /// </summary>
    public const int DefaultOrder = AdviceAddValidation.DefaultOrder + 5_000_000;
}

/// <summary>
///     Validates every <see cref="ResourceReferenceAttribute" /> property on the entity
///     against the registered <see cref="IResourceTypeResolver" />.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             Typed references (<see cref="ResourceReferenceAttribute.Target" /> non-<see langword="null" />)
///             require <see cref="IResourceTypeResolver.Resolve(string)" /> to return the exact
///             target type. A mismatch throws
///             <see cref="SchemataResourceErrors.NotFound(System.Type, string?, string?, string)" /> for
///             the target type so callers see <c>NOT_FOUND</c> with a populated
///             <see cref="Schemata.Abstractions.Errors.ResourceInfoDetail" />.
///         </item>
///         <item>
///             Polymorphic references (<see cref="ResourceReferenceAttribute.Target" /> <see langword="null" />)
///             only require the resolver to return any non-<see langword="null" /> type;
///             unresolvable values are collected into a
///             <see cref="ValidationException" /> with
///             <see cref="ErrorFieldViolation.Reason" /> <c>INVALID_REFERENCE</c>.
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

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct = default) {
        return Task.FromResult(Validate(ctx, entity));
    }

    private static AdviseResult Validate(AdviceContext ctx, TEntity entity) {
        var references = References.Value;
        if (references.Count == 0) {
            return AdviseResult.Continue;
        }

        var resolver = ctx.ServiceProvider.GetService<IResourceTypeResolver>();
        if (resolver is null) {
            return AdviseResult.Continue;
        }

        List<ErrorFieldViolation>? violations = null;

        foreach (var reference in references) {
            if (reference.Property.GetValue(entity) is not string value || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            var resolved = resolver.Resolve(value);

            if (reference.Target is not null) {
                if (resolved == reference.Target) {
                    continue;
                }

                throw SchemataResourceErrors.NotFound(reference.Target, value);
            }

            if (resolved is not null) {
                continue;
            }

            violations ??= [];
            violations.Add(new ErrorFieldViolation {
                Field       = reference.Property.Name,
                Reason      = SchemataResources.INVALID_REFERENCE,
                Description = LocalizedMessageFormatter.FormatInvariant(
                    SchemataResources.INVALID_REFERENCE,
                    new Dictionary<string, string> { ["value"] = value }),
            });
        }

        if (violations is { Count: > 0 }) {
            throw new ValidationException(violations);
        }

        return AdviseResult.Continue;
    }

    private static IReadOnlyList<Reference> BuildReferences() {
        var list = new List<Reference>();
        foreach (var property in typeof(TEntity).GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            if (property.GetIndexParameters().Length > 0) {
                continue;
            }

            var attribute = property.GetCustomAttribute<ResourceReferenceAttribute>(inherit: true);
            if (attribute is null) {
                continue;
            }

            if (property.PropertyType != typeof(string)) {
                continue;
            }

            list.Add(new Reference(property, attribute.Target));
        }

        return list;
    }

    #region Nested type: Reference

    private sealed record Reference(PropertyInfo Property, Type? Target);

    #endregion
}
