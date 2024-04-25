using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Schemata.Entity.Cache;

class PropertyVisitor : ExpressionVisitor
{
    private readonly Type               _type;
    private readonly List<PropertyInfo> _properties = [];

    public PropertyVisitor(Type type) {
        _type = type;
    }

    public IReadOnlyList<PropertyInfo> Properties => _properties;

    public static IReadOnlyList<PropertyInfo> GetProperties<T>(Expression expression) {
        var visitor = new PropertyVisitor(typeof(T));
        visitor.Visit(expression);
        return visitor.Properties;
    }

    protected override Expression VisitMember(MemberExpression node) {
        var property = node.Member as PropertyInfo;
        if (property != null && _type.IsAssignableFrom(property.DeclaringType)) {
            _properties.Add(property);
        }

        return base.VisitMember(node);
    }
}
