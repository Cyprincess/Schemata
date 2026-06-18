// Portions adapted from linq2db
// https://github.com/linq2db/linq2db/blob/fcf358851ac47c9d7a6ef2dd99e9561edd7fa985/Source/LinqToDB/Metadata/SystemComponentModelDataAnnotationsSchemaAttributeReader.cs
// Licensed under the MIT License.
// Copyright (c) 2024 Igor Tkachev, Ilya Chudin, Svyatoslav Danyliv, Dmitry Lukashenko

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using LinqToDB.Extensions;
using LinqToDB.Mapping;
using LinqToDB.Metadata;
using Schemata.Abstractions;
using ColumnAttribute = System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
using ConcurrencyCheckAttribute = System.ComponentModel.DataAnnotations.ConcurrencyCheckAttribute;
using EfPrimaryKey = Microsoft.EntityFrameworkCore.PrimaryKeyAttribute;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     LINQ to DB metadata reader that translates <c>System.ComponentModel.DataAnnotations.Schema</c>
///     attributes and the EF Core 7+ class-level
///     <see cref="EfPrimaryKey" /> into LINQ to DB mapping attributes.
/// </summary>
/// <remarks>
///     Translates <see cref="System.ComponentModel.DataAnnotations.Schema.TableAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.Schema.ColumnAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedAttribute" />, and
///     <see cref="EfPrimaryKey" /> into their LINQ to DB equivalents, and maps
///     <see cref="ConcurrencyCheckAttribute" /> to
///     <see cref="global::LinqToDB.Mapping.OptimisticLockPropertyAttribute" /> with
///     <see cref="global::LinqToDB.Mapping.VersionBehavior.Guid" /> so EF Core's native
///     concurrency token drives LINQ to DB's optimistic-update predicate.
///     <see cref="System.ComponentModel.DataAnnotations.KeyAttribute" /> is intentionally NOT
///     translated; declare keys with class-level <c>[PrimaryKey]</c> on the entity.
/// </remarks>
public sealed class SystemComponentModelDataAnnotationsSchemaAttributeReader : IMetadataReader
{
    #region IMetadataReader Members

    /// <summary>
    ///     Returns LINQ to DB mapping attributes for the specified type by reading
    ///     <see cref="System.ComponentModel.DataAnnotations.Schema.TableAttribute" /> and the
    ///     EF Core class-level <c>PrimaryKeyAttribute</c>.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>An array of mapping attributes, or an empty array if no relevant attributes are found.</returns>
    public MappingAttribute[] GetAttributes(Type type) {
        var attributes = new List<MappingAttribute>();

        var t = type.GetAttribute<TableAttribute>();
        if (t is not null) {
            var attr = new global::LinqToDB.Mapping.TableAttribute { IsColumnAttributeRequired = false };

            var name = t.Name;

            if (string.IsNullOrWhiteSpace(name)) {
                attributes.Add(attr);
            } else {
                var names = name.Replace("[", "").Replace("]", "").Split('.');

                switch (names.Length) {
                    case 0:
                        break;
                    case 1:
                        attr.Name = names[0];
                        break;
                    case 2:
                        attr.Name   = names[0];
                        attr.Schema = names[1];
                        break;
                    default:
                        throw new MetadataException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1019), name, type.FullName));
                }

                attributes.Add(attr);
            }
        }

        // EF Core class-level [PrimaryKey] is projected into LinqToDB PrimaryKey marks
        // per-member from GetAttributes(Type, MemberInfo); nothing to add at the type level.

        return attributes.ToArray();
    }

    /// <summary>
    ///     Returns LINQ to DB mapping attributes for the specified member by reading
    ///     <c>System.ComponentModel.DataAnnotations</c> attributes, and the EF Core 7+ class-level
    ///     <c>PrimaryKeyAttribute</c> when the member name appears in its <c>PropertyNames</c> list.
    /// </summary>
    /// <param name="type">The declaring type.</param>
    /// <param name="member">The member to inspect.</param>
    /// <returns>An array of mapping attributes, or an empty array if no relevant attributes are found.</returns>
    public MappingAttribute[] GetAttributes(Type type, MemberInfo member) {
        if (member.HasAttribute<NotMappedAttribute>()) {
            return [new NotColumnAttribute()];
        }

        var attributes = new List<MappingAttribute>();

        var classKey = type.GetCustomAttribute<EfPrimaryKey>(true);
        if (classKey is not null) {
            var order = 0;
            foreach (var name in classKey.PropertyNames) {
                if (string.Equals(name, member.Name, StringComparison.Ordinal)) {
                    attributes.Add(new PrimaryKeyAttribute(order));
                    break;
                }

                order++;
            }
        }

        var g = member.GetAttribute<DatabaseGeneratedAttribute>();
        if (g is {
            DatabaseGeneratedOption: DatabaseGeneratedOption.Identity,
        }) {
            attributes.Add(new IdentityAttribute());
        }

        var c = member.GetAttribute<ColumnAttribute>();
        if (c is not null) {
            attributes.Add(new global::LinqToDB.Mapping.ColumnAttribute {
                Name   = c.Name,
                DbType = c.TypeName,
            });
        }

        if (member.HasAttribute<ConcurrencyCheckAttribute>()) {
            attributes.Add(new OptimisticLockPropertyAttribute(VersionBehavior.Guid));
        }

        return attributes.ToArray();
    }

    /// <inheritdoc cref="IMetadataReader.GetDynamicColumns" />
    public MemberInfo[] GetDynamicColumns(Type type) { return []; }

    /// <summary>
    ///     Returns a unique identifier for this metadata reader instance.
    /// </summary>
    /// <returns>A string identifier.</returns>
    public string GetObjectID() { return $".{nameof(SystemComponentModelDataAnnotationsSchemaAttributeReader)}."; }

    #endregion
}
