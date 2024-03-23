using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Schemata.Abstractions.Modular;

namespace Schemata.Modular;

public class DefaultModulesProvider : IModulesProvider
{
    private static readonly List<ModuleInfo> Modules = [];

    public DefaultModulesProvider() {
        if (Modules.Count != 0) {
            return;
        }

        var entry = Assembly.GetEntryAssembly();
        if (entry is null) {
            throw new InvalidOperationException("Entry assembly is null.");
        }

        var modules = entry.GetCustomAttributes<ModuleAttribute>();

        foreach (var module in modules) {
            var assembly = Assembly.Load(module.Name);

            var type = assembly.GetTypes()
                               .Where(t => typeof(IModule).IsAssignableFrom(t))
                               .FirstOrDefault(t => !t.IsAbstract);

            if (type is null) {
                continue;
            }

            var display     = assembly.GetCustomAttribute<AssemblyTitleAttribute>();
            var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>();
            var version     = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            Modules.Add(new ModuleInfo(       //
                module.Name,                  //
                assembly, type,               //
                display?.Title,               //
                description?.Description,     //
                version?.InformationalVersion //
            ));
        }
    }

    #region IModulesProvider Members

    public IEnumerable<ModuleInfo> GetModules() {
        return Modules;
    }

    #endregion
}
