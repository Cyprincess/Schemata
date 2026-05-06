namespace Schemata.Abstractions.Modular;

/// <summary>
///     Base class for modules providing default ordering: both
///     <see cref="IFeature.Order" /> and <see cref="IFeature.Priority" />
///     default to 0, placing unconfigured modules at the front of the pipeline.
/// </summary>
public abstract class ModuleBase : IModule
{
    #region IModule Members

    public virtual int Order => 0;

    public virtual int Priority => Order;

    #endregion
}
