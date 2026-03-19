using System;
using System.Linq.Expressions;

namespace Schemata.Resource.Foundation.Grammars;

public class FilterFunction
{
    public FilterFunction(Func<Expression[], Container, Expression> factory) { Factory = factory; }

    public Func<Expression[], Container, Expression> Factory { get; }
}
