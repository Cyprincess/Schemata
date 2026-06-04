using System;
using System.Linq;

namespace Schemata.Expressions.Skeleton;

public interface IOrderCompiler
{
    string Language { get; }

    Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(string source, ExpressionCompileOptions? options = null);
}
