namespace Schemata.Abstractions.Modular;

/// <summary>
///     Base class for modules providing default ordering behavior.
/// </summary>
public abstract class ModuleBase : IModule
{
    #region IModule Members

    /// <inheritdoc />
    public virtual int Order => 0;

    /// <inheritdoc />
    public virtual int Priority => Order;

    #endregion
}
