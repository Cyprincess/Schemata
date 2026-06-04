using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Schemata.Expressions.Skeleton;

public sealed class ExpressionFunction
{
    public ExpressionFunction(Func<IReadOnlyList<Expression>, Expression> factory) {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public Func<IReadOnlyList<Expression>, Expression> Factory { get; }

    public Expression Build(IReadOnlyList<Expression> args) { return Factory(args); }
}
