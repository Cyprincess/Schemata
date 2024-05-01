using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Schemata.Entity.Cache;

class PropertyVisitor(Type type) : ExpressionVisitor
{
    private readonly List<PropertyInfo> _properties = [];

    public IReadOnlyList<PropertyInfo> Properties => _properties;

    public static IReadOnlyList<PropertyInfo> GetProperties<T>(Expression expression) {
        var visitor = new PropertyVisitor(typeof(T));
        visitor.Visit(expression);
        return visitor.Properties;
    }

    protected override Expression VisitMember(MemberExpression node) {
        var property = node.Member as PropertyInfo;
        if (property != null && type.IsAssignableFrom(property.DeclaringType)) {
            _properties.Add(property);
        }

        return base.VisitMember(node);
    }
}
