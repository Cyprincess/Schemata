using System;
using System.Collections.Generic;

namespace Schemata.Modular;

public interface IModulesProvider
{
    public IEnumerable<Type> GetModules();
}
