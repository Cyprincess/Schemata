using System.Collections.Generic;

namespace Schemata.Modular;

public interface IModulesProvider
{
    IEnumerable<ModuleDescriptor> GetModules();
}
