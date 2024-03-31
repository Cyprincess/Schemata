using System.Collections.Generic;
using Schemata.Abstractions.Modular;

namespace Schemata.Modular;

public interface IModulesProvider
{
    IEnumerable<ModuleDescriptor> GetModules();
}
