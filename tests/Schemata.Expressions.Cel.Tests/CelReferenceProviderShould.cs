using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class CelReferenceProviderShould
{
    private readonly CelCompiler          _compiler = new();
    private readonly CelReferenceProvider _provider = new();

    [Fact]
    public void Analyze_FilteredMapWithBareIdentifierPredicate_VisitsPredicateAsExternalReference() {
        var references = _provider.Analyze(_compiler.Parse("items.map(x, enabled, x.secret)"));

        Assert.Contains(references.References, reference => reference.Path is { Count: 1 } path && path[0] is "enabled");
    }

    [Fact]
    public void Analyze_FilteredMapWithBareIdentifierPredicate_BindsIterationVariableForTransform() {
        var references = _provider.Analyze(_compiler.Parse("items.map(x, enabled, x.secret)"));

        Assert.Contains(references.References,
                        reference => reference.Path is { Count: 3 } path
                                  && path[0] is "items"
                                  && path[1] is ExpressionPathMarker.Iteration
                                  && path[2] is "secret");
    }

    [Fact]
    public void Analyze_TwoVariableMacro_KeepsTwoVariableBinding() {
        var references = _provider.Analyze(_compiler.Parse("labels.exists(k, v, v.owner == 'ops')"));

        Assert.Contains(references.References,
                        reference => reference.Path is { Count: 3 } path
                                  && path[0] is "labels"
                                  && path[1] is ExpressionPathMarker.Element
                                  && path[2] is "owner");
    }
}
