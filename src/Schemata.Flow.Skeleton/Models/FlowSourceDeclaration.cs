using System;
using System.Linq.Expressions;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Declares source projection members for a binding on a <see cref="ProcessDefinition" />.</summary>
/// <param name="BindingName">The binding name the runtime uses to resolve the source.</param>
/// <param name="SourceType">The bound source entity CLR type.</param>
/// <param name="Projection">The explicit source projection mode, when configured.</param>
/// <param name="StateMember">The source member receiving the projected state, when configured.</param>
/// <param name="LifecycleMember">The source member receiving the projected lifecycle, when configured.</param>
public sealed record FlowSourceDeclaration(
    string                BindingName,
    Type                  SourceType,
    FlowSourceProjection? Projection      = null,
    LambdaExpression?     StateMember     = null,
    LambdaExpression?     LifecycleMember = null);