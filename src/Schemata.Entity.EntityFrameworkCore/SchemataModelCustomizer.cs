using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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

    /// <inheritdoc />
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

        foreach (var index in clrType.GetCustomAttributes<IndexAttribute>(true)) {
            modelBuilder.Entity(clrType).HasIndex(index.Properties).IsUnique(index.IsUnique);
        }

        foreach (var property in clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            if (property.GetIndexParameters().Length > 0) {
                continue;
            }

            TryConfigureJsonConverter(modelBuilder, clrType, property);
        }
    }

    private static void TryConfigureJsonConverter(
        ModelBuilder modelBuilder,
        Type         entityClrType,
        PropertyInfo property) {
        var declared = property.PropertyType;

        if (!JsonColumnTypes.IsSupported(declared)) {
            return;
        }

        var converterType = typeof(EfCoreJsonValueConverter<>).MakeGenericType(declared);

        modelBuilder.Entity(entityClrType)
                    .Property(property.Name)
                    .HasConversion(converterType)
                    .Metadata.SetValueComparer(JsonValueComparers.Create(declared));
    }
}
