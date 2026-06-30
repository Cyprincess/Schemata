// Portions adapted from linq2db
// https://github.com/linq2db/linq2db/blob/fcf358851ac47c9d7a6ef2dd99e9561edd7fa985/Source/LinqToDB/Metadata/SystemComponentModelDataAnnotationsSchemaAttributeReader.cs
// Licensed under the MIT License.
// Copyright (c) 2024 Igor Tkachev, Ilya Chudin, Svyatoslav Danyliv, Dmitry Lukashenko

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using LinqToDB;
using LinqToDB.Extensions;
using LinqToDB.Mapping;
using LinqToDB.Metadata;
using Schemata.Abstractions;
using Schemata.Entity.Repository.Conversions;
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
///     Key discovery uses class-level <c>[PrimaryKey]</c> declarations on the entity.
    ///     Supported scalar dictionary and scalar collection properties receive a JSON
    ///     <see cref="LinqToDbJsonConverter{T}" />, mirroring the EF Core bridge.
/// </remarks>
public sealed class SystemComponentModelDataAnnotationsSchemaAttributeReader : IMetadataReader
{
    #region IMetadataReader Members

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
                        throw new MetadataException(string.Format(SchemataResources.GetResourceString(SchemataResources.INVALID_TABLE_NAME), name, type.FullName));
                }

                attributes.Add(attr);
            }
        }

        // EF Core class-level [PrimaryKey] is projected into LinqToDB PrimaryKey marks
        // per-member from GetAttributes(Type, MemberInfo); nothing to add at the type level.

        return attributes.ToArray();
    }

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

        // [ResourceReference] is intentionally not projected to a LinqToDB AssociationAttribute:
        // AssociationAttribute targets navigation properties (declared type == the related entity),
        // not the canonical-name scalar foreign key. Referential integrity is enforced at write time
        // by AdviceValidateResourceReferences in the repository pipeline.

        if (TryGetMemberType(member) is { } memberType
         && TryGetJsonConverterType(memberType) is { } converterType) {
            attributes.Add(new ValueConverterAttribute {
                ConverterType = converterType,
            });

            // Force LinqToDB to materialize the property as a TEXT column even though its CLR type
            // is not a primitive; without the explicit column attribute, CreateTable<T> silently
            // skips collection / dictionary properties because they have no built-in SQL mapping.
            if (!attributes.Exists(a => a is global::LinqToDB.Mapping.ColumnAttribute)) {
                attributes.Add(new global::LinqToDB.Mapping.ColumnAttribute {
                    Name     = member.Name,
                    DataType = DataType.Text,
                    DbType   = "TEXT",
                });
            }
        }

        return attributes.ToArray();
    }

    private static Type? TryGetMemberType(MemberInfo member) {
        return member switch {
            PropertyInfo property => property.PropertyType,
            FieldInfo field       => field.FieldType,
            _                     => null,
        };
    }

    private static Type? TryGetJsonConverterType(Type memberType) {
        return JsonColumnTypes.IsSupported(memberType)
            ? typeof(LinqToDbJsonConverter<>).MakeGenericType(memberType)
            : null;
    }

    public MemberInfo[] GetDynamicColumns(Type type) { return []; }

    public string GetObjectID() { return $".{nameof(SystemComponentModelDataAnnotationsSchemaAttributeReader)}."; }

    #endregion
}
