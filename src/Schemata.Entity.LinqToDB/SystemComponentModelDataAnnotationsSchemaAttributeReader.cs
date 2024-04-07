// Some code is borrowed from linq2db's SystemComponentModelDataAnnotationsSchemaAttributeReader.cs
// https://github.com/linq2db/linq2db/blob/33b9d1369f34395b2c922f57ae41ac0b7b6324fa/Source/LinqToDB/Metadata/SystemComponentModelDataAnnotationsSchemaAttributeReader.cs
// The borrowed code is licensed under the MIT License:
//
// Copyright (c) 2024 Igor Tkachev, Ilya Chudin, Svyatoslav Danyliv, Dmitry Lukashenko
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Reflection;
using LinqToDB.Extensions;
using LinqToDB.Mapping;
using LinqToDB.Metadata;

namespace Schemata.Entity.LinqToDB;

public class SystemComponentModelDataAnnotationsSchemaAttributeReader : IMetadataReader
{
    #region IMetadataReader Members

    public MappingAttribute[] GetAttributes(Type type) {
        var t = type.GetAttribute<System.ComponentModel.DataAnnotations.Schema.TableAttribute>();

        if (t == null) {
            return Array.Empty<MappingAttribute>();
        }

        var attr = new TableAttribute { IsColumnAttributeRequired = false };

        var name = t.Name;

        if (string.IsNullOrWhiteSpace(name)) {
            return [attr];
        }

        var names = name.Replace("[", "").Replace("]", "").Split('.');

        switch (names.Length) {
            case 0: break;
            case 1:
                attr.Name = names[0];
                break;
            case 2:
                attr.Name   = names[0];
                attr.Schema = names[1];
                break;
            default:
                throw new MetadataException($"Invalid table name '{name}' of type '{type.FullName}'");
        }

        return [attr];
    }

    public MappingAttribute[] GetAttributes(Type type, MemberInfo member) {
        var c = member.GetAttribute<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>();
        if (c != null) {
            var attr = new ColumnAttribute {
                Name = c.Name,
                DbType = c.TypeName,
            };

            return [attr];
        }

        if (member.HasAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>()) {
            var attr = new NotColumnAttribute();

            return [attr];
        }

        return Array.Empty<MappingAttribute>();
    }

    /// <inheritdoc cref="IMetadataReader.GetDynamicColumns" />
    public MemberInfo[] GetDynamicColumns(Type type) {
        return Array.Empty<MemberInfo>();
    }

    public string GetObjectID() {
        return $".{nameof(SystemComponentModelDataAnnotationsSchemaAttributeReader)}.";
    }

    #endregion
}
