using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.EntityFrameworkCore.Conversions;

namespace Schemata.Entity.EntityFrameworkCore;

/// <summary>
///     EF Core <see cref="IModelCustomizer" /> decorator that applies Schemata-wide entity
///     conventions after <see cref="DbContext.OnModelCreating" />:
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             For every property carrying a typed
///             <see cref="ResourceReferenceAttribute" /> (<see cref="ResourceReferenceAttribute.Target" />
///             non-<see langword="null" />), configures an alternate key on the target
///             entity's <see cref="ICanonicalName.CanonicalName" /> and a foreign-key
///             relationship from the annotated property to that alternate key.
///         </item>
///         <item>
///             For every property whose declared type is
///             <see cref="Dictionary{TKey, TValue}" /> with string keys and string values,
///             or <see cref="ICollection{T}" /> of strings, registers a JSON
///             <see cref="EfCoreJsonValueConverter{T}" /> so EF Core stores the value as a
///             single text column.
///         </item>
///     </list>
///     Polymorphic <see cref="ResourceReferenceAttribute" /> properties
///     (<see cref="ResourceReferenceAttribute.Target" /> <see langword="null" />) are
///     intentionally skipped at the ORM layer; integrity is enforced at write time by
///     the resource-validating advisor.
/// </remarks>
public sealed class SchemataModelCustomizer : ModelCustomizer
{
    /// <summary>
    ///     Initializes a new <see cref="SchemataModelCustomizer" />.
    /// </summary>
    /// <param name="dependencies">EF Core service dependencies for the base customizer.</param>
    public SchemataModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context) {
        base.Customize(modelBuilder, context);

        foreach (var entity in modelBuilder.Model.GetEntityTypes()) {
            ApplyConventions(modelBuilder, entity);
        }
    }

    private static void ApplyConventions(ModelBuilder modelBuilder, IMutableEntityType entity) {
        var clrType = entity.ClrType;

        foreach (var property in clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            if (property.GetIndexParameters().Length > 0) {
                continue;
            }

            var reference = property.GetCustomAttribute<ResourceReferenceAttribute>(inherit: true);
            if (reference?.Target is { } target) {
                TryConfigureResourceReferenceForeignKey(modelBuilder, clrType, property, target);
            }

            TryConfigureJsonConverter(modelBuilder, clrType, property);
        }
    }

    private static void TryConfigureResourceReferenceForeignKey(
        ModelBuilder  modelBuilder,
        Type          dependentClrType,
        PropertyInfo  property,
        Type          principalClrType) {
        if (!typeof(ICanonicalName).IsAssignableFrom(principalClrType)) {
            return;
        }

        var principal = modelBuilder.Model.FindEntityType(principalClrType);
        if (principal is null) {
            return;
        }

        var canonical = principal.FindProperty(nameof(ICanonicalName.CanonicalName));
        if (canonical is null) {
            return;
        }

        var altKey = principal.FindKey(new[] { canonical });
        if (altKey is null) {
            modelBuilder.Entity(principalClrType).HasAlternateKey(nameof(ICanonicalName.CanonicalName));
        }

        modelBuilder.Entity(dependentClrType)
                    .HasOne(principalClrType, navigationName: null)
                    .WithMany()
                    .HasForeignKey(property.Name)
                    .HasPrincipalKey(nameof(ICanonicalName.CanonicalName))
                    .OnDelete(DeleteBehavior.NoAction);
    }

    private static void TryConfigureJsonConverter(
        ModelBuilder modelBuilder,
        Type         entityClrType,
        PropertyInfo property) {
        var declared = property.PropertyType;

        if (!IsSupportedJsonType(declared)) {
            return;
        }

        var converterType = typeof(EfCoreJsonValueConverter<>).MakeGenericType(declared);

        modelBuilder.Entity(entityClrType)
                    .Property(property.Name)
                    .HasConversion(converterType);
    }

    private static bool IsSupportedJsonType(Type type) {
        if (type == typeof(string)) {
            return false;
        }

        if (type == typeof(Dictionary<string, string>)) {
            return true;
        }

        return typeof(ICollection<string>).IsAssignableFrom(type);
    }
}
