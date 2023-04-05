namespace Schemata.Abstractions;

public interface IFeature
{
    int Order { get; }

    int Priority { get; }
}
