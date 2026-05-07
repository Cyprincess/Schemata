// Portions adapted from linq2db
// https://github.com/linq2db/linq2db/blob/fcf358851ac47c9d7a6ef2dd99e9561edd7fa985/Source/LinqToDB/Metadata/SystemComponentModelDataAnnotationsSchemaAttributeReader.cs
// Licensed under the MIT License.
// Copyright (c) 2024 Igor Tkachev, Ilya Chudin, Svyatoslav Danyliv, Dmitry Lukashenko

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using LinqToDB.Extensions;
using LinqToDB.Mapping;
using LinqToDB.Metadata;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     LINQ to DB metadata reader that translates <c>System.ComponentModel.DataAnnotations.Schema</c> attributes into
///     LINQ to DB mapping attributes.
/// </summary>
/// <remarks>
///     Translates <see cref="System.ComponentModel.DataAnnotations.Schema.TableAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.Schema.ColumnAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute" />,
///     <see cref="System.ComponentModel.DataAnnotations.KeyAttribute" />, and
///     <see cref="System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedAttribute" />
///     into their LINQ to DB equivalents.
/// </remarks>
public sealed class SystemComponentModelDataAnnotationsSchemaAttributeReader : IMetadataReader
{
    #region IMetadataReader Members

    /// <summary>
    ///     Returns LINQ to DB mapping attributes for the specified type by reading
    ///     <see cref="System.ComponentModel.DataAnnotations.Schema.TableAttribute" />.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>An array of mapping attributes, or an empty array if no relevant attributes are found.</returns>
    public MappingAttribute[] GetAttributes(Type type) {
        var t = type.GetAttribute<System.ComponentModel.DataAnnotations.Schema.TableAttribute>();

        if (t is null) {
            return [];
        }

        var attr = new TableAttribute { IsColumnAttributeRequired = false };

        var name = t.Name;

        if (string.IsNullOrWhiteSpace(name)) {
            return [attr];
        }

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

        return [attr];
    }

    /// <summary>
    ///     Returns LINQ to DB mapping attributes for the specified member by reading
    ///     <c>System.ComponentModel.DataAnnotations</c> attributes.
    /// </summary>
    /// <param name="type">The declaring type.</param>
    /// <param name="member">The member to inspect.</param>
    /// <returns>An array of mapping attributes, or an empty array if no relevant attributes are found.</returns>
    public MappingAttribute[] GetAttributes(Type type, MemberInfo member) {
        if (member.HasAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>()) {
            return [new NotColumnAttribute()];
        }

        var attributes = new List<MappingAttribute>();

        var keys = member.GetCustomAttributes<TableKeyAttribute>(true).ToList();
        if (keys.Count > 0) {
            foreach (var tk in keys.OrderBy(a => a.Order)) {
                attributes.Add(new PrimaryKeyAttribute(tk.Order));
            }
        }

        var g = member.GetAttribute<System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedAttribute>();
        if (g is {
            DatabaseGeneratedOption: System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedOption.Identity,
        }) {
            attributes.Add(new IdentityAttribute());
        }

        var c = member.GetAttribute<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>();
        if (c is not null) {
            attributes.Add(new ColumnAttribute {
                Name   = c.Name,
                DbType = c.TypeName,
            });
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
