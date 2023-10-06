namespace Schemata.Abstractions.Modular;

public abstract class ModuleBase : IModule
{
    #region IModule Members

    public virtual int Order => 0;

    public virtual int Priority => Order;

    #endregion
}
