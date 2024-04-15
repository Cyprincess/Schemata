namespace Schemata.Resource.Foundation.Grammars.Terms;

public interface IValue : IField
{
    object? Value { get; }
}
