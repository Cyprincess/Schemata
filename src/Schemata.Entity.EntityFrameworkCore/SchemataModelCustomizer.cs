using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore.Conversions;
using Schemata.Entity.Repository.Conversions;
using IndexAttribute = Schemata.Abstractions.Entities.IndexAttribute;
using PrimaryKeyAttribute = Schemata.Abstractions.Entities.PrimaryKeyAttribute;

namespace Schemata.Entity.EntityFrameworkCore;

/// <summary>
///     EF Core <see cref="IModelCustomizer" /> decorator that applies Schemata-wide entity
///     conventions after <see cref="DbContext.OnModelCreating" />.
/// </summary>
/// <remarks>
///     For every supported scalar dictionary or scalar collection property, registers a JSON
///     <see cref="EfCoreJsonValueConverter{T}" /> and a value comparer so EF Core stores the
///     value as a single text column and detects in-place mutation.
/// </remarks>
public sealed class SchemataModelCustomizer : ModelCustomizer
{
    /// <summary>
    ///     Initializes a new <see cref="SchemataModelCustomizer" />.
    /// </summary>
    /// <param name="dependencies">EF Core service dependencies for the base customizer.</param>
    public SchemataModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context) {
        base.Customize(modelBuilder, context);

        foreach (var entity in modelBuilder.Model.GetEntityTypes()) {
            ApplyConventions(modelBuilder, entity);
        }
    }

    private static void ApplyConventions(ModelBuilder modelBuilder, IMutableEntityType entity) {
        var clrType = entity.ClrType;

        if (clrType.GetCustomAttribute<PrimaryKeyAttribute>(true) is { } key) {
            modelBuilder.Entity(clrType).HasKey(key.Properties);
        }

        foreach (var index in DeclaredIndexes(clrType)) {
            var builder = modelBuilder.Entity(clrType).HasIndex(index.Properties).IsUnique(index.IsUnique);
            if (index.Filter is not null) builder.HasFilter(index.Filter);
        }

        foreach (var property in AppDomainTypeCache.GetProperties(clrType).Values) {
            if (property.GetIndexParameters().Length > 0) {
                continue;
            }

            TryConfigureJsonConverter(modelBuilder, clrType, property);
        }
    }

    /// <summary>
    ///     Yields the index declarations that apply to the entity's own table. A subtype that declares
    ///     an index over the same property list replaces the inherited declaration, so a host-defined
    ///     table can restate an inherited uniqueness rule with its own partition filter.
    /// </summary>
    private static IEnumerable<IndexAttribute> DeclaredIndexes(Type type) {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null; current = current.BaseType) {
            foreach (var index in current.GetCustomAttributes<IndexAttribute>(false)) {
                if (declared.Add(string.Join('\u001f', index.Properties))) {
                    yield return index;
                }
            }
        }
    }

    private static void TryConfigureJsonConverter(
        ModelBuilder modelBuilder,
        Type         entityClrType,
        PropertyInfo property) {
        var declared = property.PropertyType;

        // Conventions run after the application's OnModelCreating, so the effective model
        // already reflects [NotMapped] and Ignore() exclusions and any application conversion.
        // Excluded members must not be resurrected through Property(name).
        var entityType = modelBuilder.Model.FindEntityType(entityClrType);
        if (property.GetCustomAttribute<NotMappedAttribute>() is not null
            || entityType?.IsIgnored(property.Name) == true) {
            return;
        }

        var metadata = entityType?.FindProperty(property.Name);
        if (!JsonColumnTypes.IsSupported(declared)
            && !IsDeclaredNestedValueColumn(property, declared)) {
            return;
        }

        // Conventions run after the application's OnModelCreating, so an application-configured
        // conversion is the explicit contract for this member across every JSON-eligible shape;
        // the automatic JSON conversion never replaces it or its comparer.
        if (metadata?.GetValueConverter() is not null) {
            return;
        }

        var converterType = typeof(EfCoreJsonValueConverter<>).MakeGenericType(declared);

        modelBuilder.Entity(entityClrType)
                    .Property(property.Name)
                    .HasConversion(converterType)
                    .Metadata.SetValueComparer(JsonValueComparers.Create(declared));
    }

    /// <summary>
    ///     A property with an explicit <c>[Column]</c> declaration carries an explicit persistence
    ///     contract even outside the automatic JSON eligibility set. The JSON fallback applies only
    ///     to nested dictionary / collection shapes, so scalar shapes (including nullable and
    ///     native provider scalars) and binary arrays keep their existing mappings.
    /// </summary>
    private static bool IsDeclaredNestedValueColumn(PropertyInfo property, Type declared) {
        return property.GetCustomAttribute<ColumnAttribute>() is not null && IsNestedValueShape(declared);
    }

    private static bool IsNestedValueShape(Type type) {
        var actual = Nullable.GetUnderlyingType(type) ?? type;

        if (actual == typeof(byte[]) || actual == typeof(char[])) {
            return false;
        }

        if (actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
            return true;
        }

        if (actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(ICollection<>)) {
            return true;
        }

        return actual.GetInterfaces()
                     .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));
    }
}
