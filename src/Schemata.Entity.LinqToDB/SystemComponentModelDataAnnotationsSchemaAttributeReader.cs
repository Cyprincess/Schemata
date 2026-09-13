// Portions adapted from linq2db
// https://github.com/linq2db/linq2db/blob/fcf358851ac47c9d7a6ef2dd99e9561edd7fa985/Source/LinqToDB/Metadata/SystemComponentModelDataAnnotationsSchemaAttributeReader.cs
// Licensed under the MIT License.
// Copyright (c) 2024 Igor Tkachev, Ilya Chudin, Svyatoslav Danyliv, Dmitry Lukashenko

using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using LinqToDB;
using LinqToDB.Extensions;
using LinqToDB.Mapping;
using LinqToDB.Metadata;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository.Conversions;
using ColumnAttribute = System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
using ConcurrencyCheckAttribute = System.ComponentModel.DataAnnotations.ConcurrencyCheckAttribute;
using PrimaryKeyAttribute = Schemata.Abstractions.Entities.PrimaryKeyAttribute;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     LINQ to DB metadata reader that translates <c>System.ComponentModel.DataAnnotations.Schema</c>
///     attributes and Schemata class-level key and index declarations into LINQ to DB mapping attributes.
/// </summary>
/// <remarks>
///     Translates <see cref="System.ComponentModel.DataAnnotations.Schema.TableAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.Schema.ColumnAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedAttribute" />, and
    ///     <see cref="Abstractions.Entities.PrimaryKeyAttribute" /> and <see cref="IndexAttribute" /> into their LINQ to DB equivalents, and maps
    ///     <see cref="ConcurrencyCheckAttribute" /> to
    ///     <see cref="global::LinqToDB.Mapping.OptimisticLockPropertyAttribute" /> with
    ///     <see cref="global::LinqToDB.Mapping.VersionBehavior.Guid" /> so EF Core's native
    ///     concurrency token drives LINQ to DB's optimistic-update predicate.
    ///     Property discovery follows EF Core model defaults: a property maps when it has a public
    ///     instance getter, a setter (private is sufficient), and no index parameters. Explicit
    ///     interface implementations, private getters, and read-only properties are excluded unless
    ///     <c>[Column]</c>, class-level <c>[SchemataPrimaryKey]</c> membership, or
    ///     <c>[DatabaseGenerated(Identity)]</c> marks the member as an intended column.
    ///     Key discovery uses class-level <c>[SchemataPrimaryKey]</c> declarations on the entity.
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

        return [.. attributes];
    }

    public MappingAttribute[] GetAttributes(Type type, MemberInfo member) {
        if (member.HasAttribute<NotMappedAttribute>()) {
            return [new NotColumnAttribute()];
        }

        var classKey = type.GetCustomAttribute<PrimaryKeyAttribute>(true);
        var g        = member.GetAttribute<DatabaseGeneratedAttribute>();
        var c        = member.GetAttribute<ColumnAttribute>();

        // The exclusion NotColumn carries the member name so an explicit column for the same member
        // keeps its own attribute identity in the mapping and is not silently dropped by this
        // default exclusion.
        if (member is PropertyInfo property
         && !IsCandidateProperty(property)
         && c is null
         && g is not { DatabaseGeneratedOption: DatabaseGeneratedOption.Identity }
         && !InClassKey(classKey, member)) {
            return [new NotColumnAttribute { MemberName = member.Name }];
        }

        var attributes = new List<MappingAttribute>();

        if (classKey is not null) {
            var order = 0;
            foreach (var name in classKey.Properties) {
                if (string.Equals(name, member.Name, StringComparison.Ordinal)) {
                    attributes.Add(new global::LinqToDB.Mapping.PrimaryKeyAttribute(order));
                    break;
                }

                order++;
            }
        }

        if (g is {
            DatabaseGeneratedOption: DatabaseGeneratedOption.Identity,
        }) {
            attributes.Add(new IdentityAttribute());
        }

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

        return [.. attributes];
    }

    private static bool IsCandidateProperty(PropertyInfo property) {
        return property.GetGetMethod(true) is { IsPublic: true, IsStatic: false }
            && property.GetSetMethod(true) is not null
            && property.GetIndexParameters().Length == 0;
    }

    private static bool InClassKey(PrimaryKeyAttribute? key, MemberInfo member) {
        return key?.Properties.Any(name => string.Equals(name, member.Name, StringComparison.Ordinal)) == true;
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
