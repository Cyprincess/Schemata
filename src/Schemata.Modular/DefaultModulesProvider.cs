using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Schemata.Abstractions.Modular;

namespace Schemata.Modular;

public class DefaultModulesProvider : IModulesProvider
{
    private static List<Type> _modules = new();

    public DefaultModulesProvider() {
        if (_modules.Any()) {
            return;
        }

        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null) {
            throw new InvalidOperationException("Entry assembly is null.");
        }

        var assemblies = assembly.GetCustomAttributes<ModuleAttribute>().Select(m => m.Name).ToList();

        _modules = assemblies.Select(Assembly.Load)
                             .SelectMany(a => a.GetTypes())
                             .Where(t => !string.IsNullOrWhiteSpace(t.Assembly.GetName().Name))
                             .Where(t => typeof(IModule).IsAssignableFrom(t))
                             .Where(t => !t.IsAbstract)
                             .ToList();
    }

    #region IModulesProvider Members

    public IEnumerable<Type> GetModules() {
        return _modules;
    }

    #endregion
}
