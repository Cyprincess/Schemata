using System.Collections.Generic;

namespace Schemata.Expressions.Skeleton;

/// <summary>Reports accesses and structural result provenance before expression lowering.</summary>
public interface IExpressionReferenceProvider
{
    ExpressionReferences Analyze(IExpressionTree tree);
}

public sealed record ExpressionReferences(IReadOnlyList<ExpressionReference> References, ExpressionShape Result);

/// <summary>A source-rooted path of string members, typed constant keys, and structural iteration markers.</summary>
public sealed record ExpressionReference(IReadOnlyList<object> Path, bool DynamicIndex = false,
    bool MayBeLiteral = false, bool Repeated = false, bool TypeLiteral = false);

public enum ExpressionPathMarker { Element, Iteration }

/// <summary>Structural provenance of a value produced by an expression.</summary>
public abstract record ExpressionShape
{
    public sealed record Scalar : ExpressionShape;
    public sealed record Null : ExpressionShape;
    public sealed record MapValues(ExpressionShape Value) : ExpressionShape;
    public sealed record Concatenation(ExpressionShape Left, ExpressionShape Right) : ExpressionShape;
    public sealed record LiteralMap(IReadOnlyDictionary<object, ExpressionShape> Fields) : ExpressionShape;
    public sealed record Reference(ExpressionReference Access) : ExpressionShape;
    public sealed record Sequence(IReadOnlyList<ExpressionShape> Items) : ExpressionShape;
    public sealed record Map(IReadOnlyDictionary<string, ExpressionShape> Fields) : ExpressionShape;
    public sealed record Alternatives(IReadOnlyList<ExpressionShape> Values) : ExpressionShape;
}
