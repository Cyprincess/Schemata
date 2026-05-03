namespace Schemata.Abstractions.Modular;

/// <summary>
///     Marker interface for modular application modules. Extends
///     <see cref="IFeature" /> so modules participate in ordering.
/// </summary>
public interface IModule : IFeature;
