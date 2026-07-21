using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Owner.Advisors;

/// <summary>Order constants for <see cref="AdviceValidateResourceReferenceExistence{TEntity}" />.</summary>
public static class AdviceValidateResourceReferenceExistence
{
    /// <summary>
    ///     Default execution order: runs immediately after
    ///     <see cref="AdviceValidateResourceReferences.DefaultOrder" /> so type resolvability
    ///     is established before any existence query executes.
    /// </summary>
    public const int DefaultOrder = AdviceValidateResourceReferences.DefaultOrder + 10_000_000;
}

/// <summary>
///     Verifies that every <see cref="ResourceReferenceAttribute" /> property opting in via
///     <see cref="ResourceReferenceAttribute.ValidateExistence" /> points at a row that exists.
/// </summary>
/// <remarks>
///     <para>
///         Existence queries run against the referenced entity's repository by
///         <see cref="ICanonicalName.CanonicalName" /> with owner filtering suppressed
///         (<see cref="QueryOwnerSuppressed" />), so cross-owner references resolve. The
///         repository must be registered in the container; a missing registration surfaces as
///         <see cref="InvalidOperationException" />. A missing row throws
///         <see cref="SchemataResourceErrors.NotFound(System.Type, string?, string?, string)" />
///         for the target type, matching the shape produced by typed-reference type mismatches.
///     </para>
///     <para>
///         Polymorphic references resolving to a type that does not implement
///         <see cref="ICanonicalName" /> cannot be queried by canonical name and are skipped.
///         Typed references declaring <see cref="ResourceReferenceAttribute.ValidateExistence" />
///         against such a target fail fast at metadata scan with
///         <see cref="InvalidOperationException" />.
///     </para>
///     <para>
///         This advisor lives in the ownership package because suppressing the owner-scoped
///         query filter requires the <see cref="QueryOwnerSuppressed" /> marker, which
///         Schemata.Entity.Repository cannot reference without a dependency cycle.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type being added or updated.</typeparam>
public sealed class AdviceValidateResourceReferenceExistence<TEntity> :
    IRepositoryAddAdvisor<TEntity>,
    IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    private static readonly Lazy<IReadOnlyList<Reference>> References = new(BuildReferences);

    private static readonly MethodInfo ExistsMethod = typeof(AdviceValidateResourceReferenceExistence<TEntity>).GetMethod(
        nameof(ExistsAsync),
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly ConcurrentDictionary<Type, Func<AdviceContext, string, CancellationToken, Task<bool>>>
        ExistsDelegates = new();

    #region IRepositoryAddAdvisor<TEntity>, IRepositoryUpdateAdvisor<TEntity> Members

    /// <inheritdoc cref="IRepositoryAddAdvisor{TEntity}" />
    public int Order => AdviceValidateResourceReferenceExistence.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct = default) {
        var references = References.Value;
        if (references.Count == 0) {
            return AdviseResult.Continue;
        }

        var resolver = ctx.ServiceProvider.GetService<IResourceTypeResolver>();
        if (resolver is null) {
            var reference = references[0];
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name}.{reference.Property.Name} requires {nameof(IResourceTypeResolver)} for {nameof(ResourceReferenceAttribute.ValidateExistence)}.");
        }

        foreach (var reference in references) {
            if (reference.Property.GetValue(entity) is not string value || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            var target = reference.Target ?? resolver.Resolve(value);
            if (target is null || !typeof(ICanonicalName).IsAssignableFrom(target)) {
                continue;
            }

            if (await ExistsDelegate(target)(ctx, value, ct)) {
                continue;
            }

            throw SchemataResourceErrors.NotFound(target, value);
        }

        return AdviseResult.Continue;
    }

    #endregion

    private static IReadOnlyList<Reference> BuildReferences() {
        var list = new List<Reference>();
        foreach (var property in typeof(TEntity).GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            if (property.GetIndexParameters().Length > 0) {
                continue;
            }

            var attribute = property.GetCustomAttribute<ResourceReferenceAttribute>(true);
            if (attribute is null || !attribute.ValidateExistence) {
                continue;
            }

            if (property.PropertyType != typeof(string)) {
                continue;
            }

            if (attribute.Target is not null && !typeof(ICanonicalName).IsAssignableFrom(attribute.Target)) {
                throw new InvalidOperationException(
                    $"{typeof(TEntity).Name}.{property.Name} declares {nameof(ResourceReferenceAttribute)} with {nameof(ResourceReferenceAttribute.ValidateExistence)} against '{attribute.Target.Name}', which must implement {nameof(ICanonicalName)}.");
            }

            list.Add(new(property, attribute.Target));
        }

        return list;
    }

    private static Func<AdviceContext, string, CancellationToken, Task<bool>> ExistsDelegate(Type target) {
        return ExistsDelegates.GetOrAdd(
            target,
            static t => (Func<AdviceContext, string, CancellationToken, Task<bool>>)ExistsMethod
                .MakeGenericMethod(t)
                .CreateDelegate(typeof(Func<AdviceContext, string, CancellationToken, Task<bool>>)));
    }

    private static async Task<bool> ExistsAsync<TTarget>(
        AdviceContext     ctx,
        string            canonicalName,
        CancellationToken ct
    ) where TTarget : class, ICanonicalName {
        var repository = ctx.ServiceProvider.GetRequiredService<IRepository<TTarget>>();
        using (repository.AdviceContext.Use<QueryOwnerSuppressed>()) {
            return await repository.AnyAsync<TTarget>(
                query => query.Where(candidate => candidate.CanonicalName == canonicalName),
                ct);
        }
    }

    #region Nested type: Reference

    private sealed record Reference(PropertyInfo Property, Type? Target);

    #endregion
}
